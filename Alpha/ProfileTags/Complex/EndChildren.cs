using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Zeta.Bot.Profile;
using Zeta.Bot.Profile.Composites;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.TreeSharp;
using Zeta.XmlEngine;
using Action = Zeta.TreeSharp.Action;
using System.Diagnostics;

namespace QuestTools.ProfileTags
{

    /// <summary>
    /// Immediately ends all child tags when condition is satisfied</summary>
    /// <returns>
    [XmlElement("EndChildren")]
    public class EndChildrenTag : ComplexNodeTag
    {

        #region XML Attributes

        /// <summary>
        /// Distance to search for markers when used with 'ObjectiveMarkerFound' 
        /// Important for connected static areas that share a worldId.
        /// </summary>
        [XmlAttribute("distance")]
        [XmlAttribute("maxDistance")]
        [XmlAttribute("maxSearchDistance")]        
        public int maxDistance { get; set; }

        /// <summary>
        /// The when="" atribute must match one of these
        /// </summary>
        public enum EndChildrenConditionType
        {
            ObjectiveMarkerFound = 0
        }

        [XmlAttribute("when")]
        [XmlAttribute("conditionType")]
        public EndChildrenConditionType ConditionType { get; set; }

        #endregion

        #region Local Variables

        private static Func<ProfileBehavior, bool> _BehaviorProcess;
        private static Stopwatch conditionTimer = new Stopwatch();
        private static Stopwatch debounceTimer = new Stopwatch();
        private const int conditionInterval = 4000;
        private const int debounceInterval = 400;

        #endregion

        public EndChildrenTag() { }

        /// <summary>
        /// Wraps original child nodes</summary>
        /// <returns>
        protected override Composite CreateBehavior()
        {
            return
            new Decorator(ret => !IsDone,
                new PrioritySelector(
                    GetNodes().Select(b => b.Behavior).ToArray()
                )
            );
        }

        /// <summary>
        /// Checks if child nodes are finished or condition is satisfied</summary>
        /// <returns>
        public override bool IsDone
        {
            get
            {
                if (!conditionTimer.IsRunning || !debounceTimer.IsRunning)
                {
                    conditionTimer.Start();
                    debounceTimer.Start();
                }

                // These conditions don't need to be evaluated really fast.
                if (debounceTimer.ElapsedMilliseconds < debounceInterval)
                {
                    return false;
                }

                // End tag if child tags are all done.
                if (ChildrenFinished())
                {
                    conditionTimer.Stop();
                    debounceTimer.Stop();
                    return true;
                }

                // End tag if condition evaluates to True.
                if (conditionTimer.ElapsedMilliseconds > conditionInterval)
                {
                    if (CheckCondition())
                    {
                        StopBehaviors();
                        conditionTimer.Stop();
                        debounceTimer.Stop();
                        return true;
                    }

                    conditionTimer.Reset();
                }

                debounceTimer.Reset();
                return false;
            }
        }

        /// <summary>
        /// Checks to see if all child tags have finished executing</summary>
        /// <returns>
        /// Returns True if children are finished</returns> 
        internal bool ChildrenFinished()
        {

            if (_BehaviorProcess == null)
            {
                _BehaviorProcess = new Func<ProfileBehavior, bool>(p => p.IsDone);
            }

            bool allChildrenDone = Body.All<ProfileBehavior>(_BehaviorProcess);

            if (allChildrenDone)
            {
                return true;
            }

            return false;

        }

        /// <summary>
        /// Loads a condition check based on conditionType</summary>
        /// <returns>
        public bool CheckCondition()
        {
            switch (ConditionType)
            {
                case EndChildrenConditionType.ObjectiveMarkerFound:

                    return ObjectiveMarkerExists();

            }
            return false;
        }

        /// <summary>
        /// Checks if the bounty objective marker is visible</summary>
        /// <returns>
        public bool ObjectiveMarkerExists()
        {
            var marker = ZetaDia.Minimap.Markers.CurrentWorldMarkers
                            .Where(m => m.IsPointOfInterest && m.Id < 1000 && m.Position.Distance2D(ZetaDia.Me.Position) < maxDistance)
                            .OrderBy(m => m.Position.Distance2D(ZetaDia.Me.Position)).FirstOrDefault();

            var markerExists = (marker != null);

            if (markerExists)
            {
                Logger.Log("Found Objective Marker: {0} dist: {1:0} isExit: {2}",
                    marker.NameHash,
                    marker.Position.Distance2D(ZetaDia.Me.Position),
                    marker.IsPortalExit);
            }

            return markerExists;

        }

        /// <summary>
        /// Stop all child behaviors</summary>
        /// <returns>
        protected void StopBehaviors()
        {
            foreach (ProfileBehavior behavior in Body)
            {
                behavior.OnDone();
            }
        }

    }

}

