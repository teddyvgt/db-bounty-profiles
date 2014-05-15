using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Zeta.Bot.Profile;
using Zeta.Bot.Profile.Composites;
using Zeta.Game;
using Zeta.TreeSharp;
using Zeta.XmlEngine;
using Action = Zeta.TreeSharp.Action;
using QuestTools.Diagnostics;
using QuestTools.Helpers;
using Zeta.Bot;

namespace QuestTools.ProfileTags
{  

    /// <summary>
    /// XML tag for a profile to START a timer
    /// </summary>
    [XmlElement("StartTimer")]
    public class StartTimerTag : ProfileBehavior
    {
        public StartTimerTag() { }
        private bool _isDone;
        public override bool IsDone { get { return _isDone; } }

        /// <summary>
        /// The unique Identifier for this timer, used to identify what the timer is in the reports
        /// </summary>
        [XmlAttribute("name")]
        public string Name { get; set; }

        /// <summary>
        /// The group that this timer belongs to, useful for stopping multiple timers at once.
        /// </summary>
        [XmlAttribute("group")]
        public string Group { get; set; }

        protected override Composite CreateBehavior()
        {
            return new Sequence(
                new Action(ret => TimeTracker.Start(new Timing
                {
                    Name = Name,
                    StartTime = DateTime.UtcNow,
                    Group = Group,
                    QuestIsBounty = (ZetaDia.ActInfo.ActiveBounty != null),
                    QuestName = (ZetaDia.ActInfo.ActiveBounty != null) ? ZetaDia.ActInfo.ActiveBounty.Info.Quest.ToString() : ZetaDia.CurrentQuest.Name,
                    QuestId = (ZetaDia.ActInfo.ActiveBounty != null) ? ZetaDia.ActInfo.ActiveBounty.Info.QuestSNO : ZetaDia.CurrentQuest.QuestSNO
                })),
                new Action(ret => _isDone = true)
            );
        }

        public override void ResetCachedDone()
        {
            _isDone = false;
            base.ResetCachedDone();
        }
    }

    /// <summary>
    /// XML tag for a profile to STOP a timer
    /// </summary>
    [XmlElement("StopTimer")]
    public class StopTimerTag : ProfileBehavior
    {
        public StopTimerTag() { }
        private bool _isDone;
        public override bool IsDone { get { return _isDone; } }

        /// <summary>
        /// Specifying a value for name="" will the timer with that name to be stopped.
        /// </summary>
        [XmlAttribute("name")]
        public string Name { get; set; }

        /// <summary>
        /// Specifying a value for group="" will cause all timers with that group name to be stopped.
        /// </summary>
        [XmlAttribute("group")]
        public string Group { get; set; }

        protected override Composite CreateBehavior()
        {
            return new Sequence(
                new PrioritySelector(

                    new Decorator(ret => Name != null, 
                        new Action(ret => TimeTracker.StopTimer(Name))),

                    new Decorator(ret => Group != null, 
                        new Action(ret => TimeTracker.StopGroup(Group)))

                ),
                new Action(ret => _isDone = true)
            );
        }

        public override void ResetCachedDone()
        {
            _isDone = false;
            base.ResetCachedDone();
        }
    }

    /// <summary>
    /// Stops all timers.
    /// </summary>
    [XmlElement("StopAllTimers")]
    public class StopAllTimersTag : ProfileBehavior
    {
        public StopAllTimersTag() { }
        private bool _isDone;
        public override bool IsDone { get { return _isDone; } }

        protected override Composite CreateBehavior()
        {
            return new Sequence(
                new Action(ret => TimeTracker.StopAll()),
                new Action(ret => _isDone = true)
            );
        }

        public override void ResetCachedDone()
        {
            _isDone = false;
            base.ResetCachedDone();
        }
    }

}

namespace QuestTools.Diagnostics
{

    /// <summary>
    /// Keeps track of multiple timers and persists their stats accross bot sessions
    /// </summary>
    public static class TimeTracker
    {
        public static List<Timing> Timings = new List<Timing>();
        private static string _filePath = Path.Combine(FileManager.LoggingPath, String.Format("TimeTracker.csv"));
        internal static DateTime LastLoad { get; set; }
        internal static DateTime LastSave { get; set; }
        private static bool Initialized { get; set; }

        /// <summary>
        /// Since this is currently a stand-alone class (should be plugged into a plugin registration), 
        /// Immediately subscribe to DemonBuddy events and load our timing data from permanent storage.
        /// </summary>
        static TimeTracker()
        {
            try
            {
                if (!Initialized)
                {                
                    WireUp();
                    Load();
                    Initialized = true;
                }            
            }
            catch (Exception ex)
            {
                Logger.Log("TimeTracker() Exception: {0}", ex);
            }
        }


        /// <summary>
        /// Start listening to demonbuddy events.
        /// </summary>
        internal static void WireUp()
        {
            Logger.Log(">> Wire");
            BotMain.OnStart += PersistentTiming_OnStart;            
            BotMain.OnStop += PersistentTiming_OnStop;
            GameEvents.OnGameChanged += PersistentTiming_OnGameChanged;
        }

        /// <summary>
        /// Stop listening to demonbuddy events.
        /// </summary>
        internal static void UnWire()
        {
            BotMain.OnStart -= PersistentTiming_OnStart;
            BotMain.OnStop -= PersistentTiming_OnStop;
            GameEvents.OnGameChanged -= PersistentTiming_OnGameChanged;
        }

        /// <summary>
        /// When the game is stopped using the STOP button on DemonBuddy
        /// </summary>
        private static void PersistentTiming_OnStart(IBot bot)
        {
            Load();
        }

        /// <summary>
        /// When the game is started via the START button on DemonBuddy
        /// </summary>
        private static void PersistentTiming_OnStop(IBot bot)
        {            
            Save();
        }

        /// <summary>
        /// Handle when the game is 'reset' - (leaving game and starting a new one)
        /// </summary>
        private static void PersistentTiming_OnGameChanged(object sender, EventArgs e)
        {
            Timings.Where(t => t.IsRunning).ToList().ForEach(t => { t.FailedCount++; });
            StopAll();
            Save();
        }

        /// <summary>
        /// Clear the timings collection
        /// </summary>
        public static void Reset()
        {
            Timings = new List<Timing>();
        }

        /// <summary>
        /// Start a timer
        /// </summary>
        public static void Start(Timing timing)
        {
            var existingTimer = Timings.Find(t => t.Name == timing.Name);
            if (existingTimer == null)
            {
                timing.Start();
                Timings.Add(timing);
                //timing.Print("Starting Timer (New) ");
            }
            else
            {
                existingTimer.Start();
                //existingTimer.Print(string.Format("Starting Timer (Existing) AllowRestarts={0} ", existingTimer.AllowResetStartTime));
            }
        }

        /// <summary>
        /// Stop a timer
        /// </summary>
        public static bool StopTimer(string timerName)
        {
            var found = false;
            Logger.Log("Stopping Timer: {0}", timerName);
            Timings.ForEach(t =>
            {
                if (t.Name == timerName && t.IsRunning)
                {
                    found = true;
                    t.Update();
                    t.Print("Stopped Timer");
                    t.Stop();
                }
            });
            return found;
        }

        /// <summary>
        /// Stop all timers that are part of a group
        /// </summary>
        public static bool StopGroup(string groupName)
        {
            var found = false;
            Logger.Log("Stopping Group: {0}", groupName);
            Timings.ForEach(t =>
            {
                if (t.Group == groupName && t.IsRunning)
                {
                    found = true;
                    t.Update();
                    t.Print("Stopped Timer");
                    t.Stop();
                }
            });
            return found;
        }

        /// <summary>
        /// Stop all timers
        /// </summary>
        public static bool StopAll()
        {
            var found = false;
            Logger.Log("Stopping All");
            Timings.ForEach(t =>
            {
                if (t.IsRunning)
                {
                    found = true;
                    t.Update();                    
                    t.Print("Stopped Timer");
                    t.Stop();
                }
            });
            return found;
        }

        /// <summary>
        /// Load timing data from file
        /// </summary>
        private static void Load()
        {
            var output = new List<Timing>();
            try
            {
                if (File.Exists(_filePath))
                {
                    foreach (var line in File.ReadAllLines(_filePath).Skip(1))
                    {
                        var tokens = line.Split(',');
                        var t = new Timing
                        {
                            Name = tokens[0],
                            QuestId = Int32.Parse(tokens[1]),
                            QuestName = tokens[2],
                            QuestIsBounty = Boolean.Parse(tokens[3]),
                            MinTimeSeconds = Int32.Parse(tokens[5]),
                            MaxTimeSeconds = Int32.Parse(tokens[6]),
                            TimesTimed = Int32.Parse(tokens[7]),
                            TotalTimeSeconds = Int32.Parse(tokens[8]),
                            Group = tokens[9],
                            FailedCount = Int32.Parse(tokens[10])
                        };
                        output.Add(t);
                    }
                    LastLoad = DateTime.UtcNow;
                }
                Timings = output;
            }
            catch (Exception ex)
            {
                Logger.Log("Load Exception: {0}", ex);
            }

        }

        /// <summary>
        /// Save timing data to a file
        /// </summary>
        public static bool Save()
        {
            Logger.Log(">> Saving Timings");
            var saved = false;

            try
            {
                if (File.Exists(_filePath))
                {
                    //Logger.Log("File Exists, Deleting");
                    File.Delete(_filePath);
                }

                using (var w = new StreamWriter(_filePath, true))
                {
                    var line = string.Empty;
                    var format = "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\r\n";

                    var headerline = String.Format(format,
                        "Name",
                        "QuestId",
                        "QuestName",
                        "QuestIsBounty",
                        "TimeAverageSeconds",
                        "MinTimeSeconds",
                        "MaxTimeSeconds",
                        "TimesTimed",
                        "TotalTimeSeconds",
                        "Group",
                        "FailedCount"
                    );

                    w.Write(headerline);

                    Timings.ForEach(t =>
                    {
                        line = String.Format(format, 
                            t.Name, 
                            t.QuestId, 
                            t.QuestName,
                            t.QuestIsBounty,
                            t.TimeAverageSeconds,
                            t.MinTimeSeconds, 
                            t.MaxTimeSeconds,
                            t.TimesTimed,
                            t.TotalTimeSeconds,
                            t.Group,
                            t.FailedCount
                        );
                        //t.Print("Saving");
                        w.Write(line);
                    });                        
                }
                saved = true;
                LastSave = DateTime.UtcNow;
                Reset();
            }
            catch(Exception ex)
            {
                Logger.Log("Exception Saving Timer File: {0}", ex);
            }
            return saved;
        }
    }

    /// <summary>
    /// Timing Object, tracks a period of time
    /// </summary>
    public class Timing
    {
        public string Name = string.Empty;
        public int QuestId = 0;
        public string QuestName = string.Empty;
        public bool QuestIsBounty = false;
        public bool IsRunning = false;
        public bool IsStarted = false;
        public string Group = string.Empty;
        public TimeSpan Elapsed = TimeSpan.MinValue;
        public DateTime StartTime = DateTime.MinValue;
        public DateTime StopTime = DateTime.MinValue;    
        public int TimesTimed = 0;
        public int TotalTimeSeconds = 0;
        public int MaxTimeSeconds = 0;
        public int MinTimeSeconds = 0;
        public int FailedCount = 0;
        public bool AllowResetStartTime = false;

        public float TimeAverageSeconds {
            get
            {
                if (TimesTimed > 0)
                {
                    if (MinTimeSeconds == MaxTimeSeconds)
                    {
                        return MaxTimeSeconds;
                    }
                    return (float)TotalTimeSeconds / (float)TimesTimed;
                }
                return 0;
            }
        }


        public void Print()
        {
            Print(string.Empty);
        }

        /// <summary>
        /// Convert a number of seconds into a friendly time format for display
        /// </summary>
        public string FormatTime(int seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds( seconds );
            if (seconds == 0) return "0";
            var format = t.Hours > 0 ? "{0:0}h " : string.Empty;
            format += t.Minutes > 0 ? "{1:0}m " : string.Empty;
            format += t.Seconds > 0 ? "{2:0}s" : string.Empty;
            return string.Format(format,t.Hours,t.Minutes,t.Seconds);
        }

        /// <summary>
        /// Start the timer
        /// </summary>
        public void Start()
        {
            IsRunning = true;
            if (StartTime == DateTime.MinValue || AllowResetStartTime)
            {
                StartTime = DateTime.UtcNow;
            };
        }

        /// <summary>
        /// Update statistics for the timer
        /// </summary>
        public void Update()
        {
            Elapsed = DateTime.UtcNow.Subtract(this.StartTime);
            TimesTimed = TimesTimed + 1;
            TotalTimeSeconds += (int)Elapsed.TotalSeconds;
            MaxTimeSeconds = (int)Elapsed.TotalSeconds > MaxTimeSeconds ? (int)Elapsed.TotalSeconds : MaxTimeSeconds;
            MinTimeSeconds = MinTimeSeconds == 0 || (int)Elapsed.TotalSeconds < MinTimeSeconds ? (int)Elapsed.TotalSeconds : MinTimeSeconds;
        }

        /// <summary>
        /// Stop the timer
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
            StartTime = DateTime.MinValue;
            StopTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Write the current state of this timer instance to the console
        /// </summary>
        public void Print(string message)
        {
            var format = message + "Timer '{0}' Group:{11} {3} ({1}) took {10} seconds to complete (Max={5}, Min={6}, Avg={7} from {9} timings)";

            Logger.Log(format, 
                Name, 
                QuestId, 
                QuestIsBounty, 
                QuestName,
                IsRunning,
                FormatTime(MaxTimeSeconds),
                FormatTime(MinTimeSeconds),
                FormatTime((int)TimeAverageSeconds),
                TotalTimeSeconds,
                TimesTimed,
                Elapsed.TotalSeconds,
                Group,
                FailedCount

            );
        }

    }

}

namespace QuestTools.Helpers
{

    public static class FileManager
    {

        /// <summary>
        /// Gets the Logging path.
        /// </summary>
        public static void DeleteLastLine(string filepath)
        {
            List<string> lines = File.ReadAllLines(filepath).ToList();
            File.WriteAllLines(filepath, lines.GetRange(0, lines.Count - 1));
        }

        /// <summary>
        /// Gets the Logging path.
        /// </summary>
        /// <value>The path to use for logging</value>
        public static string LoggingPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_LoggingPath))
                {
                    _LoggingPath = Path.Combine(DemonBuddyPath, "TrinityLogs");
                    CreateDirectory(_LoggingPath);
                }
                return _LoggingPath;
            }
        }
        private static string _LoggingPath;

        /// <summary>
        /// Gets the DemonBuddy path.
        /// </summary>
        /// <value>The demon buddy path.</value>
        public static string DemonBuddyPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_DemonBuddyPath))
                    _DemonBuddyPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                return _DemonBuddyPath;
            }
        }
        private static string _DemonBuddyPath;

        /// <summary>
        /// Creates the directory structure.
        /// </summary>
        /// <param name="path">The path.</param>
        private static void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                if (!Directory.Exists(Path.GetDirectoryName(path)))
                {
                    CreateDirectory(Path.GetDirectoryName(path));
                }
                Directory.CreateDirectory(path);
            }
        }

    }

}
