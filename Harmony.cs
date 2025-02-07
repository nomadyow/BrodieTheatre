﻿using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using HarmonyHub;
using HarmonyHub.Entities.Response;


namespace BrodieTheatre
{
    public partial class FormMain : Form
    {
        public class Activities
        {
            public string Text;
            public string Id;

            public override string ToString()
            {
                return Text;
            }
        }
        public string currentHarmonyIP;

        private async Task HarmonyConnectAsync(bool shouldUpdate = true)
        {
            var currentActivityID = "";
            formMain.BeginInvoke(new Action(() =>
            {
                formMain.labelHarmonyStatus.Text = "Disconnected";
                formMain.labelHarmonyStatus.ForeColor = System.Drawing.Color.Maroon;
                Logging.writeLog("Harmony:  Connecting to Hub");
            }));
            try
            {
                Program.Client = await HarmonyClient.Create(Properties.Settings.Default.harmonyHubIP);
                await doDelay(2000);
                currentActivityID = await Program.Client.GetCurrentActivityAsync();
                lastHarmonyActivityID = currentActivityID.ToString();
                formMain.BeginInvoke(new Action(() =>
                {
                    formMain.labelHarmonyStatus.Text = "Connected";
                    formMain.labelHarmonyStatus.ForeColor = System.Drawing.Color.ForestGreen;
                    Logging.writeLog("Harmony:  Connected to Hub, current activity ID is '" + currentActivityID + "'");
                }));
                if (currentActivityID != "-1")
                {
                    formMain.BeginInvoke(new Action(() =>
                    {
                        if (formMain.labelRoomOccupancy.Text != "Occupied")
                        {
                            formMain.insteonDoMotion(false);
                        }
                    }));
                }
            }
            catch
            {
                formMain.BeginInvoke(new Action(() =>
                {
                    Logging.writeLog("Harmony:  Cannot connect to Harmony Hub");
                }));
                return;
            }
            await doDelay(3000);
            if (Program.Client != null)
            {
                Program.Client.OnActivityChanged += HarmonyClient_OnActivityChanged;
            }
            if (shouldUpdate)
            {
                formMain.BeginInvoke(new Action(() =>
                { 
                    formMain.HarmonyUpdateActivities(currentActivityID);
                }));           
            }
        }

        private async void HarmonyClient_OnActivityChanged(object sender, string activityID)
        {
            formMain.BeginInvoke(new Action(() =>
            {
                Logging.writeLog("Harmony:  Hub message received with current activity ID '" + activityID + "'");
                formMain.HarmonyUpdateActivities(activityID);
            }));
            if (activityID == "-1")
            {
                formMain.BeginInvoke(new Action(() =>
                {
                    formMain.projectorPowerOff();
                    formMain.lightsToEnteringLevel();
                    formMain.stopShutdownTimer();
                }));
            }
            else
            {
                await doDelay(3000);
                formMain.BeginInvoke(new Action(() =>
                {
                    if (formMain.labelRoomOccupancy.Text != "Occupied")
                    {
                        formMain.insteonDoMotion(false);
                    }
                    formMain.projectorPowerOn();
                    formMain.setDelayedLightTimer();
                    formMain.startShutdownTimer();
                    formMain.kodiLibraryScan();
                }));
            }
        }

        private async void HarmonyUpdateActivities(string currentActivityID)
        {
            int counter = 0;
            while (counter < 3)
            {
                try
                { 
                    var harmonyConfig = await Program.Client.GetConfigAsync();
                    formMain.BeginInvoke(new Action(() =>
                    {
                        formMain.listBoxActivities.Items.Clear();
                    }));
                    foreach (var activity in harmonyConfig.Activities)
                    {
                        if (activity.Id == currentActivityID)
                        {
                            formMain.BeginInvoke(new Action(() =>
                            {
                                formMain.labelCurrentActivity.Text = activity.Label;
                            }));
                            if (labelProjectorPower.Text == "On" && activity.Id == "-1")
                            {
                                // This is meant to be called only if we are in an out of sync state
                                formMain.BeginInvoke(new Action(() =>
                                {
                                    Logging.writeLog("Harmony:  Activity disabled - Powering off projector");
                                    formMain.projectorPowerOff();
                                }));
                            }
                            else if (labelProjectorPower.Text == "Off" && activity.Id != "-1")
                            {
                                // This is meant to be called only if we are in an out of sync state
                                formMain.BeginInvoke(new Action(() =>
                                {
                                    Logging.writeLog("Harmony:  Activity enabled - Powering on projector");
                                    formMain.projectorPowerOn();
                                    formMain.kodiLibraryScan();
                                    formMain.setDelayedLightTimer();
                                }));
                            }
                        }
                        else
                        {
                            Activities item = new()
                            {
                                Id = activity.Id,
                                Text = activity.Label
                            };
                            formMain.BeginInvoke(new Action(() =>
                            {
                                formMain.listBoxActivities.Items.Add(item);
                            }));
                        }
                    }
                    return;
                }
                catch
                {
                    formMain.BeginInvoke(new Action(() =>
                    {
                        Logging.writeLog("Harmony:  Cannot update Harmony Activities");
                    }));
                    counter += 1;
                }
            }
        }

        public bool HarmonyIsActivityStarted()
        {
            Logging.writeLog("Harmony:  Current Activity is: " + labelCurrentActivity.Text);
            if (labelCurrentActivity.Text == "PowerOff" || labelCurrentActivity.Text == String.Empty)
            {
                return false;
            }  
            return true;
        }

        private async void HarmonySendCommand(string device, string deviceFunction)
        {
            int counter = 0;
            while (counter < 3)
            {
                try
                {
                    var harmonyConfig = await Program.Client.GetConfigAsync();
                    foreach (Device currDevice in harmonyConfig.Devices)
                    {
                        if (currDevice.Label == device)
                        {
                            foreach (ControlGroup controlGroup in currDevice.ControlGroups)
                            {
                                foreach (Function function in controlGroup.Functions)
                                {
                                    if (function.Name == deviceFunction)
                                    {
                                        await Program.Client.SendCommandAsync(currDevice.Id, function.Name);
                                        formMain.BeginInvoke(new Action(() =>
                                        {
                                            Logging.writeLog("Harmony:  Sent Command '" + function.Name + "' to ID '" + currDevice.Id + "'");
                                        }));
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    HarmonyDispose();
                    await HarmonyConnectAsync(true);
                    counter += 1;
                }
            }
        }

        // Start Harmony Activity
        private async void HarmonyStartActivity(string activityName, string activityId, bool forceLights=true)
        {
            int counter = 0;
            while (counter < 3)
            {
                try
                {
                    formMain.BeginInvoke(new Action(() =>
                    {
                        Logging.writeLog("Harmony:  Starting Activity '" + activityName + "' Id '" + activityId + "'");
                        formMain.toolStripStatus.Text = "Starting Harmony activity - " + activityName;
                        formMain.labelCurrentActivity.Text = activityName;

                    }));
                    // Activities > 0 are those that are user driven. -1 means poweroff
                    if (Convert.ToInt32(activityId) >= 0)
                    {
                        formMain.BeginInvoke(new Action(() =>
                        {
                            formMain.projectorPowerOn();
                            formMain.kodiLibraryScan();

                            if (forceLights)
                            {
                                formMain.setDelayedLightTimer();
                            }
                        }));

                        // Delay the Harmony activity to let the projector start.  Having the amp and projector start
                        // at the same time sometimes causes the Intel graphics to go crazy
                        await doDelay(5000);
                        await Program.Client.StartActivityAsync(activityId);
                    }
                    else //Power Off
                    {
                        await Program.Client.TurnOffAsync();
                        await doDelay(1000);
                        formMain.BeginInvoke(new Action(() =>
                        {
                            //Turn up the ligths so occupants can find their way out
                            formMain.lightsToEnteringLevel();
                            formMain.projectorPowerOff();
                        }));
                    }
                    return;
                }
                catch
                {
                    HarmonyDispose();
                    await HarmonyConnectAsync(true);
                    counter += 1;
                }
            }
        }

        private void HarmonyDispose()
        {
            formMain.BeginInvoke(new Action(() =>
            {
                formMain.toolStripStatus.Text = "Closing stale Harmony Connection";
                Logging.writeLog("Harmony:  Disposing of stale connection");
                formMain.labelHarmonyStatus.Text = "Disconnected";
                formMain.labelHarmonyStatus.ForeColor = System.Drawing.Color.Maroon;
            }));
            Program.Client.OnActivityChanged -= HarmonyClient_OnActivityChanged;
            Program.Client.Dispose();
        }

        private void HarmonyStartActivityByName(string activityName, bool forceLights = true)
        {
            if (activityName == "PowerOff")
            {
                HarmonyStartActivity("PowerOff", "-1", false);
                return;
            }
            for (int i = 0; i < listBoxActivities.Items.Count; i++)
            {
                Activities currItem = (Activities)listBoxActivities.Items[i];
                if (currItem.Text == activityName)
                {      
                    HarmonyStartActivity(activityName, currItem.Id, forceLights);
                    return;
                }
            }
            Logging.writeLog("Harmony:  Unknown Activity - cound not start by name");
        }

        private async void TimerHarmonyPoll_Tick(object sender, EventArgs e)
        {
            // The first time the Timer lasts 30s.  Now poll every 60s
            timerHarmonyPoll.Interval = 60000;
            if (labelHarmonyStatus.Text == "Connected")
            {
                var currentActivityID = "";
                bool error = false;
                try
                {
                    currentActivityID = await Program.Client.GetCurrentActivityAsync();
                    if (lastHarmonyActivityID != currentActivityID.ToString())
                    {
                        formMain.BeginInvoke(new Action(() =>
                        {
                            Logging.writeLog("Harmony:  Polling - Activity Changed - reconnect");
                        }));
                        HarmonyDispose();
                        error = true;
                    }
                }
                catch (Exception ex)
                {
                    formMain.BeginInvoke(new Action(() =>
                    {
                        Logging.writeLog("Harmony:  Error Polling Hub");
                        if (debugHarmony)
                        {
                            Logging.writeLog("Harmony:  " + ex.ToString());
                        }
                    }));
                    HarmonyDispose();
                    error = true;
                }

                if (!error)
                {
                    await doDelay(3000);
                    formMain.BeginInvoke(new Action(() =>
                    {
                        if (debugHarmony)
                        {
                            Logging.writeLog("Harmony:  Poll Hub - current activity '" + currentActivityID + "'");
                        }
                        formMain.toolStripStatus.Text = "Poll Harmony Hub for updated activities";
                    }));
                    return;
                }
                else 
                {
                    Logging.writeLog("Harmony:  Error polling for activity - will atttempt reconnect");
                }
            }
            await HarmonyConnectAsync(true);
        }
    }
}