﻿using System;
using System.Windows.Forms;
using System.IO.Ports;
using System.Collections.Generic;

namespace BrodieTheatre
{
    public partial class FormMain : Form
    {
        /* My Insteon addresses
           42.22.B8 Pot
           42.20.F8 Tray
           41.66.88 Motion Sensor
           41.58.FC Door Sensor

          Mapped keypresses
           F12 - Lights to Entering level
           F11 - Lights to Stopped level
           F7  - Lights to Playback level
           F6  - Show behind screen (picture)
           F5  - Projector Lens to Narrow aspect ratio
           F4  - Projector Lens to Wide aspect ratio
           F3  - Kodi next audio language (caught by Brodietheatre Kodi Addon)
           F2  - Kodi save Aspect Ratio Override (caught by Brodietheatre Kodi Addon)
        */

        static FormMain formMain;

        static SerialPort serialPortProjector;

        private int statusTickCounter = 0;

        public string lastHarmonyActivityID = "";

        public bool debugInsteon = false;
        public bool debugHarmony = false;
        public bool debugKodi = false;
        public bool debugProjector = false;

        public List<string> panasonic_pj_labels = new()
        {
            "Lens Memory 1",
            "Lens Memory 2",
            "Lens Memory 3",
            "Lens Memory 4",
            "Lens Memory 5"
        };

        public FormMain()
        {
            hookID = SetHook(proc);
            formMain = this;
            InitializeComponent();

            serialPortProjector = new SerialPort();

            Logging.writeLog("------ Brodie Theatre Starting Up ------");
            if (Properties.Settings.Default.startMinimized)
            {
                this.WindowState = FormWindowState.Minimized;
            }
            else
            {
                this.WindowState = FormWindowState.Normal;
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private async void SettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int oldLightTimerMinutes = Properties.Settings.Default.lightingShutdownMinutes;
            Form formSettings = new FormSettings();
            formSettings.ShowDialog();

            // Reset things after the settings have been saved.
            if (currentPLMport != Properties.Settings.Default.plmPort)
            {
                currentPLMport = Properties.Settings.Default.plmPort;
                insteonConnectPLM();
            }

            if (currentHarmonyIP != Properties.Settings.Default.harmonyHubIP)
            {
                currentHarmonyIP = Properties.Settings.Default.harmonyHubIP;
                await HarmonyConnectAsync(true);
            }

            if (connectKodiLocalhost != Properties.Settings.Default.connectKodiLocalhost || currentKodiPort != Properties.Settings.Default.kodiJSONPort)
            {
                connectKodiLocalhost = Properties.Settings.Default.connectKodiLocalhost;
                currentKodiPort = Properties.Settings.Default.kodiJSONPort;
                kodiStatusDisconnect();
            }

            if (Properties.Settings.Default.lightingDelayProjectorOn > 0)
            {
                timerStartLights.Interval = Properties.Settings.Default.lightingDelayProjectorOn * 1000;
            }

            if (Properties.Settings.Default.projectorPort != currentProjectorPort)
            {
                formMain.BeginInvoke(new Action(() =>
                {
                    projectorConnect();
                    resetLightShutdownTimer();
                }));
            }

            if (oldLightTimerMinutes != Properties.Settings.Default.lightingShutdownMinutes)
            {
                formMain.BeginInvoke(new Action(() =>
                {
                    Logging.writeLog("Lighting:  Light shutdown timer changed - resetting with new value");
                    resetLightShutdownTimer();
                }));

            }
        }

        private async void FormMain_Load(object sender, EventArgs e)
        {
            formMain.BeginInvoke(new Action(() =>
            {
                formMain.timerSetLights.Enabled = true;
            }));

            currentPLMport = Properties.Settings.Default.plmPort;
            insteonConnectPLM();
            projectorConnect();

            if (labelProjectorStatus.Text == "Connected")
            {
                projectorCheckPower();
            }

            if (Properties.Settings.Default.potsAddress != string.Empty)
            {
                lights[Properties.Settings.Default.potsAddress] = -1;
            }

            if (Properties.Settings.Default.trayAddress != string.Empty)
            {
                lights[Properties.Settings.Default.trayAddress] = -1;
            }

            currentHarmonyIP = Properties.Settings.Default.harmonyHubIP;
            await HarmonyConnectAsync(true);

            if (Properties.Settings.Default.lightingDelayProjectorOn > 0)
            {
                formMain.BeginInvoke(new Action(() =>
                {
                    formMain.timerStartLights.Interval = Properties.Settings.Default.lightingDelayProjectorOn * 1000;
                }));
            }

            foreach(string s in panasonic_pj_labels)
            {
                comboBoxProjectorLensMemory.Items.Add(s);
            }
            comboBoxProjectorLensMemory.SelectedIndex = 0;
        }

        private void TimerClearStatus_Tick(object sender, EventArgs e)
        {
            if (statusTickCounter > 0)
            {
                statusTickCounter -= 1;
            }
            else
            {
                toolStripStatus.Text = "";
            }
        }

        private void ToolStripStatus_TextChanged(object sender, EventArgs e)
        {
            if (toolStripStatus.Text != string.Empty)
            {
                statusTickCounter = 2;
            }
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (Program.Client != null)
                {
                    Program.Client.Dispose();
                }
            }
            catch { }
            if (powerlineModem != null)
            {
                powerlineModem.Dispose();
            }

            UnhookWindowsHookEx(hookID);
            Logging.writeLog("------ Brodie Theatre Shutting Down ------");
        }

        private void ListBoxActivities_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Activities activity = (Activities)listBoxActivities.SelectedItem;
            HarmonyStartActivity(activity.Text, activity.Id, true);
        }

        private void LabelRoomOccupancy_TextChanged(object sender, EventArgs e)
        {
            if (labelRoomOccupancy.Text == "Occupied")
            {
                Logging.writeLog("Occupancy:  Room Occupied");

                if (!HarmonyIsActivityStarted() && labelKodiPlaybackStatus.Text == "Stopped")
                {
                    lightsToEnteringLevel();
                }

                toolStripStatus.Text = "Room is now occupied";
            }
            else if (labelRoomOccupancy.Text == "Vacant")
            {
                Logging.writeLog("Occupancy:  Room vacant");
                toolStripStatus.Text = "Room is now vacant";
            }
        }

        private void LabelRoomOccupancy_Click(object sender, EventArgs e)
        {
            if (labelRoomOccupancy.Text == "Occupied")
            {
                labelRoomOccupancy.Text = "Vacant";
                Logging.writeLog("Occupancy:  Overriding Room to Vacant");
                insteonMotionLatchActive = false;
            }
            else
            {
                labelRoomOccupancy.Text = "Occupied";
                insteonDoMotion(false);
                Logging.writeLog("Occupancy:  Overriding Room to Occupied");
            }
        }

        private void labelKodiPlaybackStatus_TextChanged(object sender, EventArgs e)
        {
            if (labelKodiPlaybackStatus.Text == "Stopped")
            {
                startShutdownTimer();
            }
            else
            {
                stopShutdownTimer();
            }
        }

        private void stopShutdownTimer ()
        {
            timerShutdown.Enabled = false;
            progressBarShutdown.Value = 0;
            Logging.writeLog("Theatre:  Stop Shutdown Timer");
        }

        private void startShutdownTimer()
        {
            if (labelKodiPlaybackStatus.Text == "Stopped" && HarmonyIsActivityStarted())
            {
                timerShutdown.Enabled = true;
                progressBarShutdown.Maximum = 60 * Properties.Settings.Default.shutdownLatch;
                progressBarShutdown.Value = progressBarShutdown.Maximum;
                Logging.writeLog("Theatre:  Starting Shutdown Timer - " + Properties.Settings.Default.shutdownLatch.ToString() + " minutes");
            }
        }

        private void labelCurrentActivity_TextChanged(object sender, EventArgs e)
        {
            if (!HarmonyIsActivityStarted())
            {
                stopShutdownTimer();
            }
        }

        private void timerShutdown_Tick(object sender, EventArgs e)
        {
            progressBarShutdown.Value--;
            if (progressBarShutdown.Value <= 0)
            {
                Logging.writeLog("Theatre:  Idle Timer Expired - Powering Down");
                stopShutdownTimer();
                if (HarmonyIsActivityStarted())
                {
                    HarmonyStartActivityByName("PowerOff");
                }
            }
        }
    }
}
