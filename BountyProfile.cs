using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using Zeta;
using Zeta.Common;
using Zeta.Common.Plugins;
using Zeta.XmlEngine;
using Zeta.TreeSharp;
using Zeta.Game;
using Zeta.Game.Internals;
using Zeta.Bot.Profile;
using Trinity.DbProvider;

namespace BountyProfile
{
    public partial class BountyProfile : IPlugin
    {
        public Version Version { get { return new Version(0, 0, 13); } }
        public string Author { get { return "Sychotix"; } }
        public string Description { get { return "Adds functionaly to make adventure profiles work."; } }
        public string Name { get { return "BountyProfile "; } }
        public bool Equals(IPlugin other) { return (other.Name == Name) && (other.Version == Version); }
        private static string pluginPath = "";
        private static string sConfigFile = "";
        private static bool bSavingConfig = false;

        public void OnEnabled()
        {
            Logger.Log("Version " + Version + " Enabled.");
        }

        public void OnDisabled()
        {
            Logger.Log("Disabled.");
        }

        public void OnInitialize()
        {
            Logger.Log("Initialized.");
        }

        public void OnPulse()
        {
        }

        public void OnShutdown()
        {

        }
        System.Windows.Window IPlugin.DisplayWindow
        {
            get
            {
                return null;
            }
        }

    } // class
    public static class Logger
    {
        private static readonly log4net.ILog Logging = Zeta.Common.Logger.GetLoggerInstanceForType();

        public static void Log(string message, params object[] args)
        {
            StackFrame frame = new StackFrame(1);
            var method = frame.GetMethod();
            var type = method.DeclaringType;

            Logging.InfoFormat("[BountyProfile] " + string.Format(message, args), type.Name);
        }

        public static void Log(string message)
        {
            Log(message, string.Empty);
        }

    }

    [XmlElement("AWTrinityExploreDungeon")]
    public class AWTrinityExploreDungeon : Trinity.XmlTags.TrinityExploreDungeon
    {

        public AWTrinityExploreDungeon() : base() {
            //Override the marker to true.
            IgnoreMarkers = false;
        }

        private bool _isDone = false;
        protected override Composite CreateBehavior()
        {
            return new PrioritySelector(
                new Decorator(ret => bountyIsDone, new Zeta.TreeSharp.Action(ret => _isDone = true)),
                base.CreateBehavior());


        }

        public override void OnStart()
        {

            if(ZetaDia.ActInfo.ActiveBounty == null) Logger.Log("Don't have an active quest. Assume we have completed it.");
            else base.OnStart(); return;
            
        }

        //Had to use ActiveBounty, caused too much lag constantly searching the bounty list
        public bool bountyIsDone
        {
            get
            {
                var b = ZetaDia.ActInfo.ActiveBounty;
                if (b == null)
                {
                    Logger.Log("Active bounty returned null, Assuming done.");
                    return true;
                }
                //If completed or on next step, we are good.
                if (b.Info.State == QuestState.Completed)
                {
                    Logger.Log("Seems completed!");
                    return true;
                }
                if (ZetaDia.IsInTown)
                {
                    return true;
                }
                if (ZetaDia.Me.IsInBossEncounter)
                {
                    return false;
                }

                
                return false;
            }
        }

        public override bool IsDone
        {
            get { return _isDone || base.IsDone; }
        }

    }

    [XmlElement("HaveBounty")]
    public class HaveBounty : Trinity.XmlTags.BaseComplexNodeTag
    {

        protected override Composite CreateBehavior()
        {
            PrioritySelector decorated = new PrioritySelector(new Composite[0]);
            foreach (ProfileBehavior behavior in base.GetNodes())
            {
                decorated.AddChild(behavior.Behavior);
            }
            return new Zeta.TreeSharp.Decorator(new CanRunDecoratorDelegate(CheckNotAlreadyDone), decorated);
        }

        public override bool GetConditionExec()
        {
            return ZetaDia.ActInfo.Bounties.Where(bounty => bounty.Info.QuestSNO == QuestSNO && bounty.Info.State != QuestState.Completed).FirstOrDefault() != null;
        }

        private bool CheckNotAlreadyDone(object obj)
        {
            return !IsDone;
        }

        [XmlAttribute("questSNO", true)]
        public int QuestSNO
        {
            get;
            set;
        }
    }

    [XmlElement("BountyAtStep")]
    public class BountyAtStep : Trinity.XmlTags.BaseComplexNodeTag
    {

        protected override Composite CreateBehavior()
        {
            PrioritySelector decorated = new PrioritySelector(new Composite[0]);
            foreach (ProfileBehavior behavior in base.GetNodes())
            {
                decorated.AddChild(behavior.Behavior);
            }
            return new Zeta.TreeSharp.Decorator(new CanRunDecoratorDelegate(CheckNotAlreadyDone), decorated);
        }

        public override bool GetConditionExec()
        {
            return ZetaDia.ActInfo.Bounties.Where(bounty => bounty.Info.QuestSNO == QuestSNO && bounty.Info.QuestStep == QuestStep && bounty.Info.State != QuestState.Completed).FirstOrDefault() != null;
        }

        private bool CheckNotAlreadyDone(object obj)
        {
            return !IsDone;
        }

        [XmlAttribute("questSNO", true)]
        public int QuestSNO
        {
            get;
            set;
        }

        [XmlAttribute("step", true)]
        public int QuestStep
        {
            get;
            set;
        }
    }

    [XmlElement("ActBountiesComplete")]
    public class ActBountiesComplete : Trinity.XmlTags.BaseComplexNodeTag
    {

        protected override Composite CreateBehavior()
        {
            PrioritySelector decorated = new PrioritySelector(new Composite[0]);
            foreach (ProfileBehavior behavior in base.GetNodes())
            {
                decorated.AddChild(behavior.Behavior);
            }
            return new Zeta.TreeSharp.Decorator(new CanRunDecoratorDelegate(CheckNotAlreadyDone), decorated);
        }

        public override bool GetConditionExec()
        {
            var b = ZetaDia.ActInfo.Bounties.Where(bounty => bounty.Act.ToString().Equals(Act) && bounty.Info.State == QuestState.Completed);
            if(b.FirstOrDefault() != null) Logger.Log("Bounties Complete count:" + b.Count());
            else Logger.Log("Bounties complete returned null.");
            return b.FirstOrDefault() != null && b.Count() == 5;
        }

        private bool CheckNotAlreadyDone(object obj)
        {
            return !IsDone;
        }

        [XmlAttribute("act", true)]
        public string Act
        {
            get;
            set;
        }
    }

} // namespace
