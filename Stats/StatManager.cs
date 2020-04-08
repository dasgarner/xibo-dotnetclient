﻿/**
 * Copyright (C) 2020 Xibo Signage Ltd
 *
 * Xibo - Digital Signage - http://www.xibo.org.uk
 *
 * This file is part of Xibo.
 *
 * Xibo is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * any later version.
 *
 * Xibo is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with Xibo.  If not, see <http://www.gnu.org/licenses/>.
 */
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XiboClient.XmdsAgents;

namespace XiboClient.Stats
{
    public sealed class StatManager
    {
        public static object _locker = new object();

        private static readonly Lazy<StatManager>
            lazy =
            new Lazy<StatManager>
            (() => new StatManager());

        /// <summary>
        /// Instance
        /// </summary>
        public static StatManager Instance { get { return lazy.Value; } }

        /// <summary>
        /// Proof of Play stats
        /// </summary>
        private Dictionary<string, Stat> proofOfPlay = new Dictionary<string, Stat>();

        /// <summary>
        /// The database path
        /// </summary>
        private string databasePath;

        /// <summary>
        /// Last time we sent stats
        /// </summary>
        private DateTime lastSendDate;

        /// <summary>
        /// A Stat Agent which we will maintain in a thread
        /// </summary>
        private StatAgent statAgent;
        private Thread statAgentThread;

        /// <summary>
        /// Init table
        /// usually run on start up
        /// </summary>
        public void InitDatabase()
        {
            // No error catching in here - if we fail to create this DB then we have big issues?
            this.databasePath = ApplicationSettings.Default.LibraryPath + @"\pop.db";

            if (!File.Exists(this.databasePath))
            {
                File.Create(this.databasePath);
            }

            using (var connection = new SqliteConnection("Filename=" + this.databasePath))
            {
                string sql = "CREATE TABLE IF NOT EXISTS stat (" +
                    "_id INTEGER PRIMARY KEY, " +
                    "fromdt TEXT, " +
                    "todt TEXT, " +
                    "type TEXT, " +
                    "scheduleId INT, " +
                    "layoutId INT, " +
                    "widgetId TEXT, " +
                    "tag TEXT, " +
                    "processing INT" +
                    ")";

                // Open the connection
                connection.Open();

                // Create an execute a command.
                using (var command = new SqliteCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Start the Stat Manager
        /// </summary>
        public void Start()
        {
            this.statAgent = new StatAgent();
            this.statAgentThread = new Thread(new ThreadStart(this.statAgent.Run));
            this.statAgentThread.Name = "StatAgentThread";
            this.statAgentThread.Start();
        }

        /// <summary>
        /// Stop the StatManager
        /// </summary>
        public void Stop()
        {
            this.statAgent.Stop();
        }

        /// <summary>
        /// Layout Start Event
        /// </summary>
        /// <param name="scheduleId"></param>
        /// <param name="layoutId"></param>
        public void LayoutStart(int scheduleId, int layoutId)
        {
            lock (_locker)
            {
                // New record, which we put in the dictionary
                string key = scheduleId + "-" + layoutId;
                Stat stat = new Stat();
                stat.Type = StatType.Layout;
                stat.From = DateTime.Now;
                stat.ScheduleId = scheduleId;
                stat.LayoutId = layoutId;

                this.proofOfPlay.Add(key, stat);
            }
        }

        /// <summary>
        /// Layout Stop Event
        /// </summary>
        /// <param name="scheduleId"></param>
        /// <param name="layoutId"></param>
        /// <param name="statEnabled"></param>
        public void LayoutStop(int scheduleId, int layoutId, bool statEnabled)
        {
            lock (_locker)
            {
                // Record we expect to already be open in the Dictionary
                string key = scheduleId + "-" + layoutId;
                Stat stat;

                if (this.proofOfPlay.TryGetValue(key, out stat))
                {
                    // Remove from the Dictionary
                    this.proofOfPlay.Remove(key);

                    // Set the to date
                    stat.To = DateTime.Now;

                    if (ApplicationSettings.Default.StatsEnabled && statEnabled)
                    {
                        // Record
                        RecordStat(stat);
                    }
                }
                else
                {
                    // This is bad, we should log it
                    Trace.WriteLine(new LogMessage("StatManager", "LayoutStop: Closing stat record without an associated opening record."), LogType.Info.ToString());
                }
            }
        }

        /// <summary>
        /// Widget Start Event
        /// </summary>
        /// <param name="scheduleId"></param>
        /// <param name="layoutId"></param>
        /// <param name="widgetId"></param>
        public void WidgetStart(int scheduleId, int layoutId, string widgetId)
        {
            lock (_locker)
            {
                // New record, which we put in the dictionary
                string key = scheduleId + "-" + layoutId + "-" + widgetId;
                Stat stat = new Stat();
                stat.Type = StatType.Media;
                stat.From = DateTime.Now;
                stat.ScheduleId = scheduleId;
                stat.LayoutId = layoutId;
                stat.WidgetId = widgetId;

                this.proofOfPlay.Add(key, stat);
            }
        }

        /// <summary>
        /// Widget Stop Event
        /// </summary>
        /// <param name="scheduleId"></param>
        /// <param name="layoutId"></param>
        /// <param name="widgetId"></param>
        /// <param name="statEnabled"></param>
        public void WidgetStop(int scheduleId, int layoutId, string widgetId, bool statEnabled)
        {
            lock (_locker)
            {
                // Record we expect to already be open in the Dictionary
                string key = scheduleId + "-" + layoutId + "-" + widgetId;
                Stat stat;

                if (this.proofOfPlay.TryGetValue(key, out stat))
                {
                    // Remove from the Dictionary
                    this.proofOfPlay.Remove(key);

                    // Set the to date
                    stat.To = DateTime.Now;

                    if (ApplicationSettings.Default.StatsEnabled && statEnabled)
                    {
                        // Record
                        RecordStat(stat);
                    }
                }
                else
                {
                    // This is bad, we should log it
                    Trace.WriteLine(new LogMessage("StatManager", "WidgetStop: Closing stat record without an associated opening record."), LogType.Info.ToString());
                }
            }
        }

        /// <summary>
        /// Records a stat record
        /// </summary>
        /// <param name="stat"></param>
        private void RecordStat(Stat stat)
        {
            try
            {
                using (var connection = new SqliteConnection("Filename=" + this.databasePath))
                {
                    connection.Open();

                    SqliteCommand command = new SqliteCommand();
                    command.Connection = connection;

                    // Parameterize
                    command.CommandText = "INSERT INTO stat (type, fromdt, todt, scheduleId, layoutId, widgetId, tag, processing) " +
                        "VALUES (@type, @fromdt, @todt, @scheduleId, @layoutId, @widgetId, @tag, @processing)";

                    command.Parameters.AddWithValue("@type", stat.Type.ToString());
                    command.Parameters.AddWithValue("@fromdt", stat.From.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"));
                    command.Parameters.AddWithValue("@todt", stat.To.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF"));
                    command.Parameters.AddWithValue("@scheduleId", stat.ScheduleId);
                    command.Parameters.AddWithValue("@layoutId", stat.LayoutId);
                    command.Parameters.AddWithValue("@widgetId", stat.WidgetId ?? "");
                    command.Parameters.AddWithValue("@tag", stat.Tag ?? "");
                    command.Parameters.AddWithValue("@processing", 0);

                    // Execute and don't wait for the result
                    command.ExecuteNonQueryAsync();

                    // TODO: should we trigger a send to happen?
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("StatManager", "RecordStat: Error saving stat to database. Ex = " + ex.Message), LogType.Error.ToString());
            }
        }

        /// <summary>
        /// TODO: Mark stat records to be sent if there are some to send
        /// </summary>
        /// <returns></returns>
        public bool MarkRecordsForSend()
        {
            return false;
        }

        /// <summary>
        /// Unmark records marked for send
        /// </summary>
        public void UnmarkRecordsForSend()
        {
            
        }

        /// <summary>
        /// Delete stats that have been sent
        /// </summary>
        public void DeleteSent()
        {

        }

        /// <summary>
        /// TODO: Get XML for the stats to send
        /// </summary>
        /// <returns></returns>
        public string GetXmlForSend()
        {
            return "";
        }

        /// <summary>
        /// TODO: Get the Total Number of Recorded Stats
        /// </summary>
        /// <returns></returns>
        public int TotalRecorded()
        {
            return 0;
        }

        /// <summary>
        /// TODO: Get the total number of stats ready to send
        /// </summary>
        /// <returns></returns>
        public int TotalReady()
        {
            return 0;
        }
    }
}