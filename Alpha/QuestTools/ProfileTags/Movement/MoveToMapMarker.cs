using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Zeta.Bot;
using Zeta.Bot.Navigation;
using Zeta.Bot.Profile;
using Zeta.Common;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.Game.Internals.Actors;
using Zeta.Game.Internals.Actors.Gizmos;
using Zeta.Game.Internals.SNO;
using Zeta.TreeSharp;
using Zeta.XmlEngine;
using Action = Zeta.TreeSharp.Action;

namespace QuestTools.ProfileTags.Movement
{
    [XmlElement("MoveToMapMarker")]
    public class MoveToMapMarker : ProfileBehavior
    {
        public MoveToMapMarker() { }
        private bool _isDone;
        /// <summary>
        /// Setting this to true will cause the Tree Walker to continue to the next profile tag
        /// </summary>
        public override bool IsDone
        {
            get
            {
                return !IsActiveQuestStep || _isDone;
            }
        }

        /// <summary>
        /// Profile Attribute to Will interact with Actor <see cref="InteractAttempts"/> times - optionally set to -1 for no interaction
        /// </summary>
        [XmlAttribute("interactAttempts")]
        public int InteractAttempts { get; set; }

        /// <summary>
        /// Profile Attribute to The exitNameHash or hash code of the map marker you wish to find, move to, and interact with
        /// </summary>
        [XmlAttribute("exitNameHash")]
        [XmlAttribute("mapMarkerNameHash")]
        [XmlAttribute("markerNameHash")]
        [XmlAttribute("portalNameHash")]
        public int MapMarkerNameHash { get; set; }

        /// <summary>
        /// Profile Attribute to set a minimum search range for your map marker or Actor near a MiniMapMarker (if it exists) or if MaxSearchDistance is not set
        /// </summary>
        [XmlAttribute("pathPrecision")]
        public float PathPrecision { get; set; }

        [XmlAttribute("straightLinePathing")]
        public bool StraightLinePathing { get; set; }

        /// <summary>
        /// Profile Attribute to set a minimum interact range for your map marker or Actor
        /// </summary>
        [XmlAttribute("interactRange")]
        public float InteractRange { get; set; }

        /// <summary>
        /// Profile Attribute to Optionally set this to true if you're using a portal. Requires use of destinationWorldId. <seealso cref="MoveToMapMarker.DestinationWorldId"/>
        /// </summary>
        [XmlAttribute("isPortal")]
        public bool IsPortal { get; set; }

        /// <summary>
        /// Profile Attribute to Optionally set this to identify an Actor for this behavior to find, moveto, and interact with
        /// </summary>
        [XmlAttribute("actorId")]
        public int ActorId { get; set; }

        /// <summary>
        /// Set this to the destination world ID you're moving to to end this behavior
        /// </summary>
        [XmlAttribute("destinationWorldId")]
        public int DestinationWorldId { get; set; }
        /// <summary>
        /// Profile Attribute that is used for very distance Position coordinates; where Demonbuddy cannot make a client-side pathing request 
        /// and has to contact the server. A value too large (usually over 300 or so) can cause pathing requests to fail or never return in un-meshed locations.
        /// </summary>
        [XmlAttribute("pathPointLimit")]
        public int PathPointLimit { get; set; }

        [XmlAttribute("x")]
        public float X { get; set; }

        [XmlAttribute("y")]
        public float Y { get; set; }

        [XmlAttribute("z")]
        public float Z { get; set; }

        [XmlAttribute("maxSearchDistance")]
        public float MaxSearchDistance { get; set; }

        /// <summary>
        /// This is the longest time this behavior can run for. Default is 600 seconds (10 minutes).
        /// </summary>
        [XmlAttribute("timeoutSeconds")]
        public int TimeoutSeconds { get; set; }

        private Vector3 _position;
        /// <summary>
        /// This is the calculated position from X,Y,Z
        /// </summary>
        public Vector3 Position
        {
            get
            {
                if (_position == Vector3.Zero)
                    _position = new Vector3(X, Y, Z);
                return _position;
            }
        }

        private bool _clientNavFailed;

        private const int TimeoutSecondsDefault = 600;
        private const int MaxStuckCountSeconds = 30;
        private const int MaxStuckRange = 15;
        private int _completedInteractAttempts;
        private int _currentStuckCount;
        private int _startWorldId = -1;

        private Vector3 _lastPosition = Vector3.Zero;
        private DateTime _behaviorStartTime = DateTime.MinValue;
        private DateTime _stuckStart = DateTime.MinValue;
        private DateTime _lastCheckedStuck = DateTime.MinValue;

        private MinimapMarker _miniMapMarker;
        private DiaObject _actor;

        private MoveResult _lastMoveResult = MoveResult.Moved;

        private MarkerSearchType _markerSearchType = MarkerSearchType.Default;
        private enum MarkerSearchType
        {
            Default = 0,
            Objective,
            Rift
        }

        /// <summary>
        /// The last seen position of the minimap marker, as it can disappear if you stand on it
        /// </summary>
        private Vector3 _mapMarkerLastPosition;

        public override void OnStart()
        {
            // set defaults
            if (Math.Abs(PathPrecision) < 1f)
                PathPrecision = 20;
            if (PathPointLimit == 0)
                PathPointLimit = 250;
            if (Math.Abs(InteractRange) < 1f)
                InteractRange = 10;
            if (InteractAttempts == 0)
                InteractAttempts = 5;
            if (TimeoutSeconds == 0)
                TimeoutSeconds = TimeoutSecondsDefault;
            if (MaxSearchDistance <= 0)
                MaxSearchDistance = 10;

            _behaviorStartTime = DateTime.UtcNow;

            _lastPosition = Vector3.Zero;
            _stuckStart = DateTime.UtcNow;
            _lastCheckedStuck = DateTime.UtcNow;
            _lastMoveResult = MoveResult.Moved;
            _completedInteractAttempts = 0;
            _startWorldId = ZetaDia.CurrentWorldId;

            Navigator.Clear();
            Logger.Debug("Initialized {0}", Status());
        }

        /// <summary>
        /// Main MoveToMapMarker Behavior
        /// </summary>
        /// <returns></returns>
        protected override Composite CreateBehavior()
        {
            return
            new Sequence(
                new DecoratorContinue(ret => ZetaDia.Me.IsDead || ZetaDia.IsLoadingWorld,
                    new Sequence(
                      new Action(ret => Logger.Log("IsDead={0} IsLoadingWorld={1}", ZetaDia.Me.IsDead, ZetaDia.IsLoadingWorld)),
                      new Action(ret => RunStatus.Failure)
                   )
               ),
                new DecoratorContinue(ret => _lastMoveResult == MoveResult.ReachedDestination && _actor == null,
                    new Sequence(
                        new Action(ret =>
                        {
                            var objective = GetObjectiveMarker();
                            var distance = objective.Position.Distance(ZetaDia.Me.Position);
                            if (objective != null && distance > PathPrecision)
                            {
                                Logger.Log("Bad! tag thinks it has reached destination but objective is {0} yards away!", distance);
                            }
                        }),
                        new DecoratorContinue(ret => _markerSearchType != MarkerSearchType.Objective || IsObjectiveInRange(),
                            new Sequence(
                                new Action(ret => Logger.Log("{0}, finished!", _lastMoveResult)),
                                new Action(ret => _isDone = true),
                                new Action(ret => RunStatus.Failure)
                            )
                        )

                    )
                ),
                CheckTimeout(),
                new Action(ret => FindMiniMapMarker()),
                new DecoratorContinue(ret => _mapMarkerLastPosition != Vector3.Zero,
                    new Action(ret => RefreshActorInfo())
                ),
                new DecoratorContinue(ret => _actor == null && _miniMapMarker == null && Position == Vector3.Zero,
                    new Sequence(
                        new Action(ret => _miniMapMarker = GetRiftExitMarker())
                    )
                ),
                new DecoratorContinue(ret => _actor == null && _miniMapMarker == null && Position == Vector3.Zero,
                    new Sequence(
                        new Action(ret => _miniMapMarker = GetObjectiveMarker())
                    )
                ),
                new DecoratorContinue(ret => _actor == null && _miniMapMarker == null && Position == Vector3.Zero,
                    new Sequence(
                        new Action(ret => Logger.Debug("Error: Could not find MiniMapMarker nor PortalObject nor Position {0}", Status())),
                        new Action(ret => _isDone = true)
                    )
                ),
                new Sequence(
                    new Action(ret => GameUI.SafeClickUIButtons()),
                    new PrioritySelector(
                        new Decorator(ret => GameUI.IsPartyDialogVisible,
                            new Action(ret => Logger.Log("Party Dialog is visible"))
                        ),
                        new Decorator(ret => GameUI.IsElementVisible(GameUI.GenericOK),
                            new Action(ret => Logger.Log("Generic OK is visible"))
                        ),
                        new Decorator(ret => DestinationWorldId == -1 && ZetaDia.CurrentWorldId != _startWorldId,
                            new Sequence(
                                new Action(ret => Logger.Log("World changed ({0} to {1}), destinationWorlId={2}, finished {3}",
                                    _startWorldId, ZetaDia.CurrentWorldId, DestinationWorldId, Status())),
                                new Action(ret => _isDone = true)
                            )
                        ),
                        new Decorator(ret => DestinationWorldId != 0 && ZetaDia.CurrentWorldId == DestinationWorldId,
                            new Sequence(
                                new Action(ret => Logger.Log("DestinationWorlId matched, finished {0}", Status())),
                                new Action(ret => _isDone = true)
                            )
                        ),
                        new Decorator(ret => _completedInteractAttempts > 1 && _lastPosition.Distance(ZetaDia.Me.Position) > 4f && DestinationWorldId != _startWorldId,
                            new Sequence(
                                new Action(ret => _isDone = true),
                                new Action(ret => Logger.Log("Moved {0:0} yards after interaction, finished {1}", _lastPosition.Distance(ZetaDia.Me.Position), Status()))
                            )
                        ),
                        new Decorator(ret => _markerSearchType == MarkerSearchType.Objective && IsValidObjective() && _actor.ActorType == ActorType.Monster && _actor.Position.Distance(ZetaDia.Me.Position) <= PathPrecision,
                            new Sequence(
                                new Action(ret => _isDone = true),
                                new Action(ret => Logger.Log("We found the objective and its a monster, ending tag so we can kill it", Status()))
                            )
                        ),
                        new DecoratorContinue(ret => _markerSearchType == MarkerSearchType.Objective && IsValidObjective() && _actor.ActorType == ActorType.Gizmo && _actor is GizmoPortal && _actor.Position.Distance(ZetaDia.Me.Position) <= PathPrecision,
                            new Sequence(
                                new Action(ret => Logger.Log("We found the objective and its a portal", Status()))
                            )
                        ),
                        new Decorator(ret => _markerSearchType == MarkerSearchType.Objective && GetObjectiveMarker() == null,
                            new Sequence(
                                new Action(ret => _isDone = true),
                                new Action(ret => Logger.Log("There is no objective marker, ending tag", Status()))
                            )
                        ),                        
                        new Decorator(ret => _actor != null && _actor.IsValid,
                            new PrioritySelector(
                                MoveToActorOutsideRange(),
                                UseActorIfInRange()
                            )
                        ),
                        new Decorator(ret => _miniMapMarker != null && _actor == null,
                            new PrioritySelector(
                                MoveToMapMarkerOnly(),
                                MoveToMapMarkerSuccess()
                            )
                        ),
                        MoveToPosition()
                    )
                )
            );
        }

        private bool IsObjectiveInRange()
        {
            var objective = GetObjectiveMarker();
            var distance = objective.Position.Distance(ZetaDia.Me.Position);
            if (objective != null && distance < PathPrecision)
            {
                return true;
            }
            return false;
        }

        private bool IsValidObjective()
        {
            try
            {
                return (_actor != null && _actor.CommonData.GetAttribute<int>(ActorAttributeType.BountyObjective) > 0);
            }
            catch (Exception ex)
            {
                Logger.Log("Exception in IsValidObjective(), {0}", ex);
            }
            return false;
        }

        private static MinimapMarker GetObjectiveMarker()
        {
            return ZetaDia.Minimap.Markers.CurrentWorldMarkers
                .Where(m => m.IsPointOfInterest && m.Id < 1000)
                .OrderBy(m => m.Position.Distance2D(ZetaDia.Me.Position)).FirstOrDefault();
        }

        private static MinimapMarker GetRiftExitMarker()
        {
            int index = DataDictionary.RiftWorldIds.IndexOf(ZetaDia.CurrentWorldId);
            if (index == -1)
                return null;

            return ZetaDia.Minimap.Markers.CurrentWorldMarkers
                .OrderBy(m => m.Position.Distance2D(ZetaDia.Me.Position))
                .FirstOrDefault(m => m.NameHash == DataDictionary.RiftPortalHashes[index]);
        }

        private void FindMiniMapMarker()
        {
            // Special condition for Rift portals
            if (MapMarkerNameHash == 0 && Position == Vector3.Zero && ActorId == 0 && IsPortal && DestinationWorldId == -1)
            {
                _miniMapMarker = GetRiftExitMarker();
                if (_miniMapMarker != null)
                {
                    MapMarkerNameHash = _miniMapMarker.NameHash;
                    Logger.Log("Using Rift Style Minimap Marker: {0} dist: {1:0} isExit: {2}",
                        _miniMapMarker.NameHash,
                        _miniMapMarker.Position.Distance2D(ZetaDia.Me.Position),
                        _miniMapMarker.IsPortalExit);
                }
            }

            // Special condition for Objective Marker
            if (MapMarkerNameHash == -1)
            {
                _markerSearchType = MarkerSearchType.Objective;
                _miniMapMarker = GetObjectiveMarker();
                if (_miniMapMarker != null)
                {

                    MapMarkerNameHash = _miniMapMarker.NameHash;
                    Logger.Log("Using Objective Style Minimap Marker: {0} dist: {1:0} isExit: {2} isEntrance {3}",
                        _miniMapMarker.NameHash,
                        _miniMapMarker.Position.Distance2D(ZetaDia.Me.Position),
                        _miniMapMarker.IsPortalExit,
                        _miniMapMarker.IsPortalEntrance);
                }
            }   

            // find our map marker
            if (_miniMapMarker == null)
            {
                if (Position != Vector3.Zero)
                {
                    _miniMapMarker = ZetaDia.Minimap.Markers.CurrentWorldMarkers
                        .Where(marker => marker != null && marker.NameHash == MapMarkerNameHash &&
                            Position.Distance(marker.Position) < MaxSearchDistance)
                        .OrderBy(o => o.Position.Distance(ZetaDia.Me.Position)).FirstOrDefault();
                }
                else
                {
                    _miniMapMarker = ZetaDia.Minimap.Markers.CurrentWorldMarkers
                        .Where(marker => marker != null && marker.NameHash == MapMarkerNameHash)
                        .OrderBy(o => o.Position.Distance(ZetaDia.Me.Position)).FirstOrDefault();
                }
            }
            if (_miniMapMarker != null && _miniMapMarker.Position != Vector3.Zero)
            {
                _mapMarkerLastPosition = _miniMapMarker.Position;
            }

        }
        private PrioritySelector CheckStuck()
        {
            return new PrioritySelector(
                new Decorator(ret => _currentStuckCount > 0 && DateTime.UtcNow.Subtract(_stuckStart).TotalSeconds > MaxStuckCountSeconds,
                    new Action(delegate
                    {
                        Logger.Debug("Looks like we're stuck since it's been {0} seconds stuck... finishing", DateTime.UtcNow.Subtract(_stuckStart).TotalSeconds);
                        _isDone = true;
                        return RunStatus.Success;
                    })
                ),
                new Decorator(ret => DateTime.UtcNow.Subtract(_lastCheckedStuck).TotalMilliseconds < 500,
                    new Action(delegate
                        {
                            return RunStatus.Success;
                        }
                    )
                ),
                new Decorator(ret => ZetaDia.Me.Position.Distance(_lastPosition) < MaxStuckRange,
                    new Action(delegate
                    {
                        _currentStuckCount++;
                        _lastCheckedStuck = DateTime.UtcNow;
                        _lastPosition = ZetaDia.Me.Position;
                        if (_currentStuckCount > DateTime.UtcNow.Subtract(_stuckStart).TotalSeconds * .5)
                            _clientNavFailed = true;

                        if (QuestTools.EnableDebugLogging)
                        {
                            Logger.Debug("Stuck count: {0}", _currentStuckCount);
                        }
                        return RunStatus.Success;
                    })
                ),
                new Decorator(ret => ZetaDia.Me.Position.Distance(_lastPosition) > MaxStuckRange,
                    new Action(delegate
                    {
                        _currentStuckCount = 0;
                        _lastCheckedStuck = DateTime.UtcNow;
                        _lastPosition = ZetaDia.Me.Position;

                        return RunStatus.Success;
                    })
                ),
                new Action(delegate
                    {
                        _lastPosition = ZetaDia.Me.Position;
                        return RunStatus.Success;
                    }
                )
            );
        }
        private DecoratorContinue CheckTimeout()
        {
            return
            new DecoratorContinue(ret => Math.Abs(DateTime.UtcNow.Subtract(_behaviorStartTime).TotalSeconds) > TimeoutSeconds,
                new Sequence(
                    new Action(ret => _isDone = true),
                    new Action(ret => Logger.Log("Timeout of {0} seconds exceeded in current behavior", TimeoutSeconds)),
                    new Action(ret => RunStatus.Failure)
                )
            );
        }

        private Decorator MoveToMapMarkerSuccess()
        {
            return // moved to map marker successfully
            new Decorator(ret => _miniMapMarker != null && _miniMapMarker.Position.Distance(ZetaDia.Me.Position) < PathPrecision,
                new Action(delegate
                    {
                        Logger.Debug("Successfully Moved to Map Marker {0}, distance: {1} {2}", _miniMapMarker.NameHash, _miniMapMarker.Position.Distance(ZetaDia.Me.Position), Status());
                        _isDone = true;
                        return RunStatus.Success;
                    }
                )
            );
        }
        private void RefreshActorInfo()
        {
            Vector3 myPos = ZetaDia.Me.Position;

            if ((_actor == null || (_actor != null && !_actor.IsValid)) && ActorId != 0)
            {
                _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true)
                    .Where(o => o.IsValid && o.ActorSNO == ActorId && ActorWithinRangeOfMarker(o))
                    .OrderBy(DistanceToMapMarker)
                    .FirstOrDefault();
            }
            if (_actor != null && _actor.IsValid)
            {
                if (QuestTools.EnableDebugLogging)
                {
                    Logger.Debug("Found actor {0} {1} {2} of distance {3} from point {4}",
                                        _actor.ActorSNO, _actor.Name, _actor.ActorType, _actor.Position.Distance(_mapMarkerLastPosition), _mapMarkerLastPosition);
                }
            }
            else if (ActorId != 0 && Position != Vector3.Zero && _position.Distance(ZetaDia.Me.Position) <= PathPrecision)
            {
                _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true).Where(o => o.IsValid && o.ActorSNO == ActorId
                   && o.Position.Distance2D(Position) <= PathPrecision).OrderBy(o => o.Distance).FirstOrDefault();
            }
            else if (ActorId != 0 && _mapMarkerLastPosition != Vector3.Zero && _mapMarkerLastPosition.Distance(myPos) <= PathPrecision)
            {
                // No ActorID defined, using Marker position to find actor
                _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true).Where(o => o != null && o.IsValid && o.ActorSNO == ActorId
                   && o.Position.Distance2D(_mapMarkerLastPosition) <= PathPrecision).OrderBy(o => o.Distance).FirstOrDefault();
            }
            else if (ActorId == 0 && _markerSearchType == MarkerSearchType.Objective && _mapMarkerLastPosition.Distance2D(myPos) <= 200)
            {
                try
                {
                    _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true)
                            .Where(a => a.CommonData != null && a.Position.Distance2D(_mapMarkerLastPosition) <= PathPrecision
                                    && (a.CommonData.GetAttribute<int>(ActorAttributeType.BountyObjective) > 0))
                            .OrderBy(a => a.Position.Distance2D(_mapMarkerLastPosition)).FirstOrDefault();
                }
                catch (Exception ex)
                {
                    Logger.Log("Failed trying to find actor {0}", ex);
                }
            }
            else if (ActorId == 0 && _mapMarkerLastPosition != Vector3.Zero && _mapMarkerLastPosition.Distance2D(myPos) <= 90)
            {
                _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true)
                    .Where(a => a.Position.Distance2D(_mapMarkerLastPosition) <= MaxSearchDistance)
                    .OrderBy(a => a.Position.Distance2D(_mapMarkerLastPosition)).FirstOrDefault();

                if (_actor != null)
                {
                    InteractRange = _actor.CollisionSphere.Radius;
                    Logger.Debug("Found Actor from Map Marker! mapMarkerPos={0} actor={1} {2} {3} {4}",
                        _mapMarkerLastPosition, _actor.ActorSNO, _actor.Name, _actor.ActorType, _actor.Position);
                }
            }
            else if (_mapMarkerLastPosition.Distance(ZetaDia.Me.Position) < PathPrecision)
            {
                if (QuestTools.EnableDebugLogging)
                {
                    Logger.Debug("Could not find an actor {0} within range {1} from point {2}",
                                       ActorId, PathPrecision, _mapMarkerLastPosition);
                }
            }

            if (ActorId == 0 && _actor != null && _markerSearchType == MarkerSearchType.Objective)
            {
                if (IsValidObjective())
                {
                    // need to lock on to a specific actor or we'll just keep finding other things near the marker.
                    ActorId = _actor.ActorSNO;
                    Logger.Log("Found our Objective Actor! mapMarkerPos={0} actor={1} {2} {3} {4}",
                        _mapMarkerLastPosition, _actor.ActorSNO, _actor.Name, _actor.ActorType, _actor.Position);
                }
            }

            if (_actor is GizmoPortal && !IsPortal)
            {
                IsPortal = true;
            }
        }

        private float DistanceToMapMarker(DiaObject o)
        {
            return o.Position.Distance(_miniMapMarker.Position);
        }

        private bool ActorWithinRangeOfMarker(DiaObject o)
        {
            bool test = false;

            if (o != null && _miniMapMarker != null)
            {
                test = o.Position.Distance(_miniMapMarker.Position) <= PathPrecision;
            }
            return test;
        }

        private Decorator MoveToActorOutsideRange()
        {
            return // move to the actor if defined and outside of InteractRange
            new Decorator(ret => _actor.Position.Distance2D(ZetaDia.Me.Position) > InteractRange,
                new Action(
                    delegate
                    {
                        Logger.Log("Moving to actor {0}, distance: {1} {2} {3}", _actor.Name, _actor.Position.Distance(ZetaDia.Me.Position), _actor.ActorSNO, Status());
                        if (!Move(_actor.Position))
                        {
                            Logger.Debug("Move result failed, we're done {0}", Status());
                            _isDone = true;
                            return RunStatus.Failure;
                        }

                        return RunStatus.Success;
                    }
                )
            );
        }

        private Decorator UseActorIfInRange()
        {
            return // use the actor if defined and within range
            new Wait(2, ret => _actor.Position.Distance(ZetaDia.Me.Position) <= InteractRange && InteractAttempts > -1 && (_completedInteractAttempts < InteractAttempts || IsPortal),
                new Sequence(
                    new Action(ret => _lastPosition = ZetaDia.Me.Position),
                    new Action(ret => _actor.Interact()),
                    new Action(ret => _completedInteractAttempts++),
                    new DecoratorContinue(ret => QuestTools.EnableDebugLogging,
                        new Action(ret => Logger.Debug("Interacting with portal object {0}, result: {1}", _actor.ActorSNO, Status()))
                    ),
                    new Sleep(500),
                    new Action(ret => GameEvents.FireWorldTransferStart())
                )
            );
        }

        private Decorator MoveToMapMarkerOnly()
        {
            return // just move to the map marker
            new Decorator(ret => _miniMapMarker != null && _miniMapMarker.Position.Distance(ZetaDia.Me.Position) > PathPrecision,
                new Action(delegate
                    {
                        bool success = Move(_miniMapMarker.Position, String.Format("Minimap Marker {0}", _miniMapMarker.NameHash));

                        if (!success)
                        {
                            Navigator.Clear();
                        }
                        else
                        {
                            //if (QuestTools.EnableDebugLogging)
                            //{
                            Logger.Log("Moving to Map Marker {0} at <{1}, {2}, {3}>,  distance: {4:0}", _miniMapMarker.NameHash, _miniMapMarker.Position.X, _miniMapMarker.Position.Y, _miniMapMarker.Position.Z, _miniMapMarker.Position.Distance(ZetaDia.Me.Position));
                            //}
                        }

                        return RunStatus.Success;
                    }
                )
            );
        }

        private Decorator MoveToPosition()
        {
            return //Position defined only - can't find map marker nor actor
            new Decorator(ret => _miniMapMarker == null && Position != Vector3.Zero,
                new Action(delegate
                    {
                        bool moveStatus = false;

                        if (Position.Distance(ZetaDia.Me.Position) > PathPrecision)
                        {
                            moveStatus = Move(Position, "Position only");
                        }
                        else
                        {
                            Logger.Debug("Position Defined only - Within {0} of destination {1}", PathPrecision, Position);
                            _isDone = true;
                        }
                        if (!moveStatus)
                        {
                            Logger.Debug("Movement failed to position {0}", Position);
                            //isDone = true;
                        }
                        return RunStatus.Success;
                    }
                )
            );
        }

        /// <summary>
        /// Move without a destination name, see <seealso cref="MoveToMapMarker.Move"/>
        /// </summary>
        /// <param name="newpos"></param>
        /// <returns></returns>
        private bool Move(Vector3 newpos)
        {
            return Move(newpos, null);
        }

        List<Vector3> allPoints = new List<Vector3>();
        List<Vector3> validPoints = new List<Vector3>();
        private QTNavigator QTNavigator = new QTNavigator();

        public MoveToMapMarker(Point destination2DPoint)
        {
        }

        /// <summary>
        /// Safely Moves the player to the requested destination <seealso cref="MoveToMapMarker.PathPointLimit"/>
        /// </summary>
        /// <param name="newpos">Vector3 of the new position</param>
        /// <param name="destinationName">For logging purposes</param>
        /// <returns></returns>
        private bool Move(Vector3 newpos, string destinationName = "")
        {
            bool result = false;

            if (StraightLinePathing)
            {
                Navigator.PlayerMover.MoveTowards(newpos);
                _lastMoveResult = MoveResult.Moved;
                result = true;
            }

            if (!ZetaDia.WorldInfo.IsGenerated)
            {
                if (_clientNavFailed && PathPointLimit > 20)
                {
                    PathPointLimit = PathPointLimit - 10;
                }
                else if (_clientNavFailed && PathPointLimit <= 20)
                {
                    PathPointLimit = 250;
                }

                if (newpos.Distance(ZetaDia.Me.Position) > PathPointLimit)
                {
                    newpos = MathEx.CalculatePointFrom(ZetaDia.Me.Position, newpos, newpos.Distance(ZetaDia.Me.Position) - PathPointLimit);
                }
            }
            float destinationDistance = newpos.Distance(ZetaDia.Me.Position);

            _lastMoveResult = QTNavigator.MoveTo(newpos, destinationName + String.Format(" distance={0:0}", destinationDistance), true);

            switch (_lastMoveResult)
            {
                case MoveResult.Moved:
                case MoveResult.ReachedDestination:
                case MoveResult.UnstuckAttempt:
                    _clientNavFailed = false;
                    result = true;
                    break;
                case MoveResult.PathGenerated:
                case MoveResult.PathGenerating:
                case MoveResult.PathGenerationFailed:
                case MoveResult.Failed:
                    Navigator.PlayerMover.MoveTowards(Position);
                    result = false;
                    _clientNavFailed = true;
                    break;
            }

            if (QuestTools.EnableDebugLogging)
            {
                Logger.Debug("MoveResult: {0}, newpos={1} Distance={2}, destinationName={3}",
                    _lastMoveResult.ToString(), newpos, newpos.Distance(ZetaDia.Me.Position), destinationName);
            }
            return result;
        }

        public bool isValid()
        {
            try
            {
                if (!ZetaDia.IsInGame || ZetaDia.IsLoadingWorld)
                    return false;

                // check if everything we need here is safe to use
                if (ZetaDia.Me != null && ZetaDia.Me.IsValid &&
                    ZetaDia.Me.CommonData != null && ZetaDia.Me.CommonData.IsValid)
                    return true;
            }
            catch
            {
            }
            return false;
        }

        public String Status()
        {
            string extraInfo = "";
            if (DataDictionary.RiftWorldIds.Contains(ZetaDia.CurrentWorldId))
                extraInfo += "IsRift ";

            if (QuestToolsSettings.Instance.DebugEnabled)
                return String.Format("questId={0} stepId={1} actorId={2} exitNameHash={3} isPortal={4} destinationWorldId={5} " +
                    "pathPointLimit={6} interactAttempts={7} interactRange={8} pathPrecision={9} x=\"{10}\" y=\"{11}\" z=\"{12}\" " + extraInfo,
                    this.QuestId, this.StepId, this.ActorId, this.MapMarkerNameHash, this.IsPortal, this.DestinationWorldId,
                    this.PathPointLimit, this.InteractAttempts, this.InteractRange, this.PathPrecision, this.X, this.Y, this.Z
                    );

            return string.Empty;
        }

        public override void ResetCachedDone()
        {
            _isDone = false;
            base.ResetCachedDone();
        }

    }
}
