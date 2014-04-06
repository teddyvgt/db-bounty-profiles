using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Windows;
using System.Diagnostics;
using Zeta;
using Zeta.Common;
using Zeta.Common.Plugins;
using Zeta.XmlEngine;
using Zeta.TreeSharp;
using Zeta.Game;
using Zeta.Game.Internals;

namespace Test
{
    public partial class Test : IPlugin
    {
        public Version Version { get { return new Version(0, 0, 2); } }
        public string Author { get { return "Sychotix"; } }
        public string Description { get { return "Spam dumps all the profiles ingame."; } }
        public string Name { get { return "Bounty Dumper "; } }
        public bool Equals(IPlugin other) { return (other.Name == Name) && (other.Version == Version); }
        private static string pluginPath = "";
        private static string sConfigFile = "";
        private static bool bSavingConfig = false;

        public void OnEnabled()
        {
            Logger.Log("Enabled.");
            foreach (var b in ZetaDia.ActInfo.Bounties)
            {
                Logger.Log("Act: " + b.Act.ToString() + " info: " + b.Info + " levelarea: " + b.LevelArea + " quest: " + b.Quest + " state: " + b.State);
            }
            Logger.Log("done.");
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
            BountyInfo b = ZetaDia.ActInfo.ActiveBounty;
            if (b == null) Logger.Log("Active Bounty Was null");
            else Logger.Log("Active: Quest:" + b.Info.Quest + " QuestSNO: " + b.Info.QuestSNO + " NumSteps: " + b.Info.QuestRecord.NumberOfSteps + " NumCompletionSteps: " + b.Info.QuestRecord.NumberOfCompletionSteps + "BounusCount: " + b.Info.BonusCount + " Kill Count " + b.Info.KillCount + " QuestMeter " + b.Info.QuestMeter);
        }

        public void OnShutdown()
        {

        }
        public Window DisplayWindow
        {
            get
            { return null; }
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

            Logging.InfoFormat("[TestThing] " + string.Format(message, args), type.Name);
        }

        public static void Log(string message)
        {
            Log(message, string.Empty);
        }

    }
} // namespace
