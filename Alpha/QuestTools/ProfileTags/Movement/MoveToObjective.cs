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
    [XmlElement("MoveToObjective")]
    public class MoveToObjective : MoveToMapMarker
    {
        public MoveToObjective() { }

        #region Properties

        /// <summary>
        /// Set the distance from marker that actors will be detected.
        /// </summary>
        [XmlAttribute("actorDistance")]
        public int ActorDistance { get; set; }

        /// <summary>
        /// Set the distance from player that markers will be detected.
        /// </summary>
        [XmlAttribute("distance")]
        [XmlAttribute("markerDistance")]
        public int MarkerDistance { get; set; }

        /// <summary>
        /// Type of objective determines how it will be interacted with
        /// </summary>
        [XmlAttribute("type")]
        [XmlAttribute("objectiveType")]
        private ObjectiveMarkerType ObjectiveType { get; set; }
        private enum ObjectiveMarkerType
        {
            Unknown = 0,
            Portal,
            Monster,
            Object
        }

        #endregion

        #region Local Variables

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
        List<Vector3> allPoints = new List<Vector3>();
        List<Vector3> validPoints = new List<Vector3>();
        private QTNavigator QTNavigator = new QTNavigator();
        private MoveResult _lastMoveResult = MoveResult.Moved;
        private bool markerFound = false;
        private Vector3 _mapMarkerLastPosition;

        #endregion

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

            if (MarkerDistance < 200)
                MarkerDistance = 3000;
            if (ActorDistance < 5)
                ActorDistance = 20;

            QuestToolsSettings.Instance.DebugEnabled = true;

            ActorId = 0;
            DestinationWorldId = -1;

            _behaviorStartTime = DateTime.UtcNow;
            _lastPosition = Vector3.Zero;
            _stuckStart = DateTime.UtcNow;
            _lastCheckedStuck = DateTime.UtcNow;
            _lastMoveResult = MoveResult.Moved;
            _completedInteractAttempts = 0;
            _startWorldId = ZetaDia.CurrentWorldId;

            Navigator.Clear();
            Logger.Debug("Initialized {0}", LogStatus());
        }

        /// <summary>
        /// Main MoveToMapMarker Behavior
        /// </summary>
        /// <returns></returns>
        protected override Composite CreateBehavior()
        {
            return
            new Sequence(

                HandleInvalidPlayer(),

                CheckDestinationReached(),

                CheckTimeout(),

                FindObjective(),
                
                FindActorNearMarker(),

                //HandleNoActorMarkerOrPosition(),

                new Action(ret => GameUI.SafeClickUIButtons()),

                new PrioritySelector(
                        
                    HandleIntefaceBlockedView(),
                    HandleWorldChanged(),

                    // Have we already interacted with it?
                    HandleInteractionCompleted(),

                    //LogContinue("HandleObjectiveTypeCheck"),

                    // What kind of actor is it?
                    HandleObjectiveIsMonster(),
                    HandleObjectiveIsPortal(),
                    HandleObjectiveIsObject(),
     
                    // does the marker still exist?
                    new Decorator(ret => GetObjectiveMiniMapMarker() == null, 
                        LogEnd("There is no objective marker")),

                    // Move to Actor location
                    new Decorator(ret => ActorExists(),
                        new PrioritySelector(
                            MoveToActorOutsideRange(),
                            UseActorIfInRange()
                        )
                    ),

                    // Move to Marker Location
                    new Decorator(ret => MarkerExists() && !ActorExists(),
                        new PrioritySelector(
                            MoveToMapMarker(),
                            MoveToMapMarkerSuccess()
                        )
                    )

                )

            );
        }


        #region Helpers

            private bool ActorExists()
            {
                return _actor != null && _actor.IsValid;
            }

            private bool MarkerExists()
            {
                return _miniMapMarker != null;
            }

            private bool IsObjectiveMonster()
            {
                return _actor.ActorType == ActorType.Monster;
            }

            private bool IsObjectivePortal()
            {
                return _actor is GizmoPortal;
            }

            private bool IsObjectiveObject()
            {
                return _actor is GizmoLootContainer;
            }

            private Sequence LogEnd(string reason)
            {
                return
                new Sequence(
                    new Action(ret => _isDone = true),
                    new Action(ret => Logger.Log(reason + " {0}", LogStatus()))
                );
            }

            private Sequence LogContinue(string reason)
            {
                return
                new Sequence(
                    new Action(ret =>
                    {
                        Logger.Log(reason + " {0}", LogStatus());
                        return RunStatus.Failure;
                    })
                );
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
                    test = o.Position.Distance(_miniMapMarker.Position) <= ActorDistance;
                }
                return test;
            }

        #endregion

        private Composite HandleInvalidPlayer()
        {
            return
            new DecoratorContinue(ret => ZetaDia.Me.IsDead || ZetaDia.IsLoadingWorld,
                new Sequence(
                    new Action(ret => Logger.Log("IsDead={0} IsLoadingWorld={1}", ZetaDia.Me.IsDead, ZetaDia.IsLoadingWorld)),
                    new Action(ret => RunStatus.Failure)
                )
            );
        }

        private Composite HandleInteractionCompleted()
        {
            return
            new Decorator(ret => _completedInteractAttempts > 1 && _lastPosition.Distance(ZetaDia.Me.Position) > 4f && DestinationWorldId != _startWorldId,
                new Sequence(
                    new Action(ret => _isDone = true),
                    new Action(ret => Logger.Log("Moved {0:0} yards after interaction, finished {1}", _lastPosition.Distance(ZetaDia.Me.Position), LogStatus()))
                )
            );
        }

        private Composite HandleNoActorMarkerOrPosition()
        {
            return
            new DecoratorContinue(ret => !ActorExists() && !MarkerExists() && Position == Vector3.Zero,
                LogEnd("Error: Could not find MiniMapMarker nor PortalObject nor Position {0}")
            );
        }


        private Decorator HandleObjectiveIsMonster()
        {
            return
            new Decorator(ret => IsValidObjective() && IsObjectiveMonster() && ActorWithinRange(_actor),
                new Sequence(
                    new Action(ret => ObjectiveType = ObjectiveMarkerType.Monster),
                    LogEnd("Found monster Objective, ending tag so we can kill it")
                )
            );
        }

        private Decorator HandleObjectiveIsPortal()
        {
            return
            new Decorator(ret => IsValidObjective() && IsObjectivePortal() && ActorWithinRange(_actor),
                new Sequence(
                    new Action(ret => ObjectiveType = ObjectiveMarkerType.Portal),
                    LogContinue("Found Portal Objective, interacting with it")
                )
            );
        }

        private Decorator HandleObjectiveIsObject()
        {
            return
            new Decorator(ret => IsValidObjective() && IsObjectiveObject() && ActorWithinRange(_actor),
                new Sequence(
                    new Action(ret => ObjectiveType = ObjectiveMarkerType.Object),
                    LogContinue("Found Object Objective, we should interact with it")
                )
            );
        }

        private PrioritySelector HandleWorldChanged()
        {
            return
            new PrioritySelector(
                new Decorator(ret => DestinationWorldId == -1 && ZetaDia.CurrentWorldId != _startWorldId,
                    new Sequence(
                        new Action(ret => Logger.Log("World changed ({0} to {1}), destinationWorlId={2}, finished {3}", _startWorldId, ZetaDia.CurrentWorldId, DestinationWorldId, LogStatus())),
                        new Action(ret => _isDone = true)
                    )
                ),
                new Decorator(ret => DestinationWorldId != 0 && ZetaDia.CurrentWorldId == DestinationWorldId,
                    new Sequence(
                        new Action(ret => Logger.Log("DestinationWorlId matched, finished {0}", LogStatus())),
                        new Action(ret => _isDone = true)
                    )
                )
            );
        }

        private PrioritySelector HandleIntefaceBlockedView()
        {
            return
            new PrioritySelector(
                new Decorator(ret => GameUI.IsPartyDialogVisible,
                    new Action(ret => Logger.Log("Party Dialog is visible"))
                ),
                new Decorator(ret => GameUI.IsElementVisible(GameUI.GenericOK),
                    new Action(ret => Logger.Log("Generic OK is visible"))
                )
            );
        }

        private Composite CheckDestinationReached()
        {
            return
            new DecoratorContinue(ret => _lastMoveResult == MoveResult.ReachedDestination && _actor == null,
                new Sequence(
                    new Action(ret =>
                    {
                        var objective = GetObjectiveMiniMapMarker();
                        var distance = objective.Position.Distance(ZetaDia.Me.Position);
                        if (objective != null && distance > InteractRange)
                        {
                            Logger.Log("Bot thinks it has reached destination but objective is {0} yards away! RequiredDistance={1} LastMoveResult={0}", distance, PathPrecision, _lastMoveResult);
                        }
                    }),
                    new Action(ret => _isDone = true),
                    new Action(ret => RunStatus.Failure)
                )
            );
        }

        private bool IsObjectiveInRange()
        {
            var objective = GetObjectiveMiniMapMarker();
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
                return (_actor != null && _actor.CommonData.GetAttribute<int>(ActorAttributeType.BountyObjective) > 0) || _actor is GizmoPortal;
            }
            catch (Exception ex)
            {
                Logger.Log("Exception in IsValidObjective(), {0}", ex);
            }
            return false;
        }

        private Action FindObjective()
        {
            return
            new Action(ret => {

                if (!markerFound)
                {
                    markerFound = true;
                    _miniMapMarker = GetObjectiveMiniMapMarker();

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

            
                if (_miniMapMarker != null && _miniMapMarker.Position != Vector3.Zero)
                {
                    _mapMarkerLastPosition = _miniMapMarker.Position;
                }

            });
        }

        private MinimapMarker GetObjectiveMiniMapMarker()
        {
            // Look for Objective Marker
            var marker = ZetaDia.Minimap.Markers.CurrentWorldMarkers
                .Where(m => m.IsPointOfInterest && m.Id < 1000)
                .OrderBy(m => m.Position.Distance2D(ZetaDia.Me.Position)).FirstOrDefault();

            if (marker == null)
            {
                // Look for Point of Interest Marker
                marker = ZetaDia.Minimap.Markers.CurrentWorldMarkers
                .Where(m => m.IsPointOfInterest)
                .OrderBy(m => m.Position.Distance2D(ZetaDia.Me.Position)).FirstOrDefault();
            }

            if (marker == null)
            {
                // If profile gave us a nameHash, then use it
                marker = ZetaDia.Minimap.Markers.CurrentWorldMarkers
                .Where(m => m.NameHash == MapMarkerNameHash)
                .OrderBy(m => m.Position.Distance2D(ZetaDia.Me.Position)).FirstOrDefault();
            }            

            return marker;
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
       
        private Composite FindActorNearMarker()
        {
            return 

            new DecoratorContinue(ret => _mapMarkerLastPosition != Vector3.Zero,

                new Action(ret => {

                    Vector3 myPos = ZetaDia.Me.Position;

                    if ((_actor == null || (_actor != null && !_actor.IsValid)) && ActorId != 0)
                    {
                        _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true)
                            .Where(o => o.IsValid && o.ActorSNO == ActorId && ActorWithinRangeOfMarker(o))
                            .OrderBy(DistanceToMapMarker)
                            .FirstOrDefault();
                    }

                    if (ActorId == 0 && _mapMarkerLastPosition.Distance2D(myPos) <= 200)
                    {
                        GetObjectiveActorNearMarker();
                    }
                    else if (ActorId == 0 && _mapMarkerLastPosition.Distance(ZetaDia.Me.Position) < PathPrecision)
                    {
                        Logger.Log("Could not find an actor {0} within range {1} from point {2}", ActorId, PathPrecision, _mapMarkerLastPosition);
                    }

                    if (ActorId == 0 && _actor != null)
                    {
                        if (IsValidObjective())
                        {
                            // need to lock on to a specific actor or we'll just keep finding other things near the marker.
                            InteractRange = (IsObjectivePortal()) ? 20 : _actor.CollisionSphere.Radius;
                            ActorId = _actor.ActorSNO;
                            Logger.Log("Found our Objective Actor! mapMarkerPos={0} actor={1} {2} {3} {4}", _mapMarkerLastPosition, _actor.ActorSNO, _actor.Name, _actor.ActorType, _actor.Position);
                        }
                    }

                    if (_actor is GizmoPortal && !IsPortal)
                    {
                        IsPortal = true;
                    }

                })
            );
        }

        private void GetObjectiveActorNearMarker()
        {
            try
            {
                // Monsters are the actual objective marker
                _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true)
                        .Where(a => a.CommonData != null && a.Position.Distance2D(_mapMarkerLastPosition) <= ActorDistance
                                && (a.CommonData.GetAttribute<int>(ActorAttributeType.BountyObjective) > 0))
                        .OrderBy(a => a.Position.Distance2D(_mapMarkerLastPosition)).FirstOrDefault();

                if (_actor == null)
                {
                    // Portals are not the objective marker but actors near the marker location
                    _actor = ZetaDia.Actors.GetActorsOfType<DiaObject>(true).Where(o => o != null && o.IsValid && o is GizmoPortal
                       && o.Position.Distance2D(_mapMarkerLastPosition) <= ActorDistance).OrderBy(o => o.Distance).FirstOrDefault();
                }

            }
            catch (Exception ex)
            {
                Logger.Log("Failed trying to find objective actor {0}", ex);
            }
        }

        private bool ActorWithinRange(DiaObject o)
        {
            bool test = false;
            if (o != null && ZetaDia.Me.Position != null)
            {
                test = o.Position.Distance(ZetaDia.Me.Position) <= PathPrecision;
            }
            return test;
        }

        private Decorator MoveToActorOutsideRange()
        {
            return 
            new Decorator(ret => _actor.Position.Distance2D(ZetaDia.Me.Position) > InteractRange,
                new Action(
                    delegate
                    {
                        Logger.Log("Moving to actor {0}, distance: {1} {2} {3}", _actor.Name, _actor.Position.Distance(ZetaDia.Me.Position), _actor.ActorSNO, LogStatus());
                        if (!Move(_actor.Position))
                        {
                            Logger.Debug("Move result failed, we're done {0}", LogStatus());
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
            return 
            new Wait(2, ret => _actor.Position.Distance(ZetaDia.Me.Position) <= InteractRange && InteractAttempts > -1 && (_completedInteractAttempts < InteractAttempts || IsPortal),
                new Sequence(
                    new Action(ret => Logger.Log("Interacting...")),
                    new Action(ret => _lastPosition = ZetaDia.Me.Position),
                    new Action(ret => _actor.Interact()),
                    new Action(ret => _completedInteractAttempts++),
                    new DecoratorContinue(ret => QuestTools.EnableDebugLogging,
                        new Action(ret => Logger.Log("Interacting with portal object {0}, result: {1}", _actor.ActorSNO, LogStatus()))
                    ),
                    new Sleep(500),
                    new Action(ret => GameEvents.FireWorldTransferStart())
                )
            );
        }

        private Decorator MoveToMapMarker()
        {
            return 
            new Decorator(ret => _miniMapMarker != null && _miniMapMarker.Position.Distance(ZetaDia.Me.Position) > InteractRange,
                new Action(delegate
                    {
                        bool success = Move(_miniMapMarker.Position, String.Format("Minimap Marker {0}", _miniMapMarker.NameHash));

                        if (!success)
                        {
                            Navigator.Clear();
                        }
                        else
                        {
                            if (QuestToolsSettings.Instance.DebugEnabled)
                            {
                                Logger.Log("Moving to Map Marker {0} at <{1}, {2}, {3}>,  distance: {4:0}", _miniMapMarker.NameHash, _miniMapMarker.Position.X, _miniMapMarker.Position.Y, _miniMapMarker.Position.Z, _miniMapMarker.Position.Distance(ZetaDia.Me.Position));
                            }
                        }

                        return RunStatus.Success;
                    }
                )
            );
        }

        private Decorator MoveToMapMarkerSuccess()
        {
            return
            new Decorator(ret => _miniMapMarker != null && _miniMapMarker.Position.Distance(ZetaDia.Me.Position) < PathPrecision,
                new Action(delegate
                {
                    Logger.Debug("Successfully Moved to Map Marker {0}, distance: {1} {2}", _miniMapMarker.NameHash, _miniMapMarker.Position.Distance(ZetaDia.Me.Position), LogStatus());
                    _isDone = true;
                    return RunStatus.Success;
                })
            );
        }

        private string LogStatus(){

            return string.Empty;
        }

        #region Movement

        /// <summary>
        /// Move without a destination name, see <seealso cref="MoveToMapMarker.Move"/>
        /// </summary>
        /// <param name="newpos"></param>
        /// <returns></returns>
        private bool Move(Vector3 newpos)
        {
            return Move(newpos, null);
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

        #endregion

        #region DB ProfileTag Overrides

        public override void ResetCachedDone()
        {
            _isDone = false;
            base.ResetCachedDone();
        }
        
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
        private bool _isDone;


        #endregion

    }
}
