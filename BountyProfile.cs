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

namespace BountyProfile
{
    public partial class BountyProfile : IPlugin
    {
        public Version Version { get { return new Version(0, 2, 2); } }
        public string Author { get { return "Sychotix"; } }
        public string Description { get { return "Adds functionaly to make adventure profiles work."; } }
        public string Name { get { return "BountyProfile "; } }
        public bool Equals(IPlugin other) { return (other.Name == Name) && (other.Version == Version); }
        private static string pluginPath = "";
        private static string sConfigFile = "";
        private static bool bSavingConfig = false;

        public void OnEnabled()
        {
            Logger.Log("Enabled.");
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

        [XmlAttribute("bountyQuestSNO", true)]
        public int QuestSNO { get; set; }

        [XmlAttribute("bountyStep", true)]
        public int Step { get; set; }

        public AWTrinityExploreDungeon() : base() {
            
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
            var b = ZetaDia.ActInfo.Bounties.Where(bounty => bounty.Info.QuestSNO == QuestSNO).FirstOrDefault();
            if(b == null) Logger.Log("Don't have quest: " + QuestSNO);
            else base.OnStart(); return;
            
        }

        //Had to use ActiveBounty, caused too much lag constantly searching the bounty list
        public bool bountyIsDone
        {
            get
            {
                if (ZetaDia.ActInfo.ActiveBounty == null)
                {
                    Logger.Log("Something went wrong, started but can't find bounty anymore. Assuming done.");
                    return true;
                }
                //If completed or on next step, we are good.
                if (ZetaDia.ActInfo.ActiveBounty.Info.State == QuestState.Completed || ZetaDia.ActInfo.ActiveBounty.Info.QuestStep != Step)
                {
                    Logger.Log("Seems completed!");
                    return true;
                }
                   
                //If Trinity thinks we are done, make sure
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

} // namespace
