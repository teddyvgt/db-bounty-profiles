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
        public Version Version { get { return new Version(0, 0, 5); } }
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
            Logger.Log("Refreshing Cache!");
            BountyCache.timer_tick(null, null);
            var b = BountyCache.getBounties().Where(bounty => bounty.Info.QuestSNO == QuestSNO).FirstOrDefault();
            if(b == null) Logger.Log("Don't have quest: " + QuestSNO);
            else base.OnStart(); return;
            
        }

        //Had to use ActiveBounty, caused too much lag constantly searching the bounty list
        public bool bountyIsDone
        {
            get
            {
                var b = BountyCache.getActiveBounty();
                if (b == null)
                {
                    Logger.Log("Something went wrong, active bounty returned null. Assuming done.");
                    return true;
                }
                //If completed or on next step, we are good.
                if (b.Info.State == QuestState.Completed || b.Info.QuestStep != Step)
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
            return BountyCache.getBounties().Where(bounty => bounty.Info.QuestSNO == QuestSNO && bounty.Info.State != QuestState.Completed).FirstOrDefault() != null;
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
            return BountyCache.getBounties().Where(bounty => bounty.Info.QuestSNO == QuestSNO && bounty.Info.QuestStep == QuestStep && bounty.Info.State != QuestState.Completed).FirstOrDefault() != null;
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
            var b = BountyCache.getBounties().Where(bounty => bounty.Act.ToString().Equals(Act) && bounty.Info.State == QuestState.Completed);
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

    [XmlElement("RefreshBountyCache")]
    public class RefreshBountyCache : ProfileBehavior
    {
        private bool m_IsDone = false;
        public override bool IsDone
        {
            get
            {
                return m_IsDone;
            }
        }

        protected override Composite CreateBehavior()
        {
            return new Zeta.TreeSharp.Action(ret =>
            {
                Log("Refreshing Cache!");
                BountyCache.timer_tick(null, null);
                m_IsDone = true;
            });
        }

        public override void ResetCachedDone()
        {
            m_IsDone = false;
            base.ResetCachedDone();
        }

        private void Log(string message, LogLevel logLevel = LogLevel.Info)
        {
            Logger.Log(message);
        }
    }

    public class BountyCache
    {
        static System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        static private bool _init;
        static IEnumerable<BountyInfo> _bounties;
        static BountyInfo _activeBounty;

        static private void init()
        {
             Logger.Log("Initializing cache!");
                timer.Tick += new EventHandler(timer_tick);
                timer.Interval = (1000) * (1);
                _bounties = ZetaDia.ActInfo.Bounties;
                if(_bounties == null) Logger.Log("Bounties returned null during init, hope that is OK");
                _activeBounty = ZetaDia.ActInfo.ActiveBounty;
                if (_activeBounty == null) Logger.Log("Active Bounty returned null during init, hope that is OK");
                timer.Enabled = true;
                timer.Start();
                _init = true;
        }

        static public IEnumerable<BountyInfo> getBounties() {
            if(!_init)
                init();
            return _bounties;
        }

        static public BountyInfo getActiveBounty()
        {
            if (!_init)
                init();
            return _activeBounty;
        }



        static public void timer_tick(object sender, EventArgs e)
        {
            //Make sure we have initialized before messing with timer
            if(_init) timer.Stop();
            _bounties = ZetaDia.ActInfo.Bounties;
            if (_bounties == null) Logger.Log("Bounties returned null during timer_tick, hope that is OK");
            _activeBounty = ZetaDia.ActInfo.ActiveBounty;
            if (_activeBounty == null) Logger.Log("Active Bounty returned null during timer_tick, hope that is OK");
            if (_init) timer.Start();
        }
        

    }

} // namespace
