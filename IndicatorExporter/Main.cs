using System;
using System.Collections.Generic;
using System.Text;

using System.Timers;
using System.Net;
using System.Net.Sockets;

using DV.HUD;
using DV.Utils;

using UnityModManagerNet;

using TinyJson;


namespace IndicatorExporter
{
    [EnableReloading]
    public static class Main
    {
        private static Settings settings;
        private static Socket socket;
        private static Timer timer;

        private static UnityModManager.ModEntry.ModLogger logger;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            logger = modEntry.Logger;
            logger.Log("Loading...");
            settings = Settings.Load<Settings>(modEntry);
            if (settings == null)
            {
                settings = new Settings();
            }
            
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                Apply(settings);
            }
            catch (Exception e)
            {
                logger.Log("Error on initialization:");
                logger.Log(e.ToString());
                logger.Log(e.StackTrace);
            }

            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnToggle = OnToggle;

            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool activated)
        {
            if (!activated)
            {
                if (timer != null)
                {
                    timer.Enabled = false;
                    timer.Stop();
                }
            }
            else
            {
                Apply(settings);
            }

            return true;
        }

        public static void Unload()
        {
            if (timer != null)
            {
                timer.Stop();
            }

            if (socket != null)
            {
                socket.Close();
            }
        }

        private static void SendUpdate(object sender, ElapsedEventArgs e)
        {
            if (settings.DebugTimer)
            {
                logger.Log("Timer debug: tick");
            }

            if (socket != null)
            {
                foreach (TrainCar loco in SingletonBehaviour<CarSpawner>.Instance.AllLocos)
                {
                    if (settings.DebugTimer)
                    {
                        logger.Log("Test vehicle: " + loco.CarGUID);
                    }

                    if (PlayerManager.LastLoco.CarGUID == loco.CarGUID)
                    {
                        if (settings.DebugTimer)
                        {
                            logger.Log("Player vehicle: " + loco.CarGUID);
                        }

                        try
                        {
                            SendPacket(loco);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex.ToString());
                        }
                        break;
                    }
                }

                if (!settings.Active)
                {
                    timer.Enabled = false;
                }
            }
        }
        
        private static void SendPacket(TrainCar loco)
        {
            Dictionary<String, String> data = new Dictionary<String, String>();
            
            data.Add("vehicle", loco.CarGUID);
            
            if(settings.SendForce && loco.SimController != null)
                data.Add("force", loco.SimController.drivingForce.generatedForce.ToString("G"));
            
            if(settings.SendThrottle && loco.SimController != null)
                data.Add("throttle", loco.SimController.controlsOverrider.Throttle.Value.ToString("G"));
            
            if(settings.SendSpeed)
                data.Add("speed", loco.GetForwardSpeed().ToString("G"));

            if (settings.SendWeight)
            {
                float weight = 0.0f;
                foreach (TrainCar car in loco.trainset.cars)
                {
                    weight += car.massController.TotalMass;
                }
                data.Add("weight",  weight.ToString("G"));
            }

            if(settings.SendRPM && loco.interior != null && loco.interior.GetComponent<InteriorControlsManager>() != null)
                data.Add("rpm", loco.interior.GetComponent<InteriorControlsManager>().indicatorReader.engineRpm.Value.ToString());

            if(settings.SendCarType)
                data.Add("plate", loco.GetComponent<TrainCarPlatesController>().carTypeText.ToString());

            if(settings.SendPosition)
                data.Add("position", (loco.transform.position - WorldMover.currentMove).ToString());

            string json = data.ToJson();
            
            if(settings.DebugTimer)
                Main.logger.Log("Sending data: " + json);
            
            byte[] buffer = ASCIIEncoding.UTF8.GetBytes(json);

            try
            {
                socket.SendTo(buffer, new IPEndPoint(IPAddress.Parse(settings.UdpHost), settings.UdpPort));
            }
            catch (Exception e)
            {
                logger.Error("Could not send packet: " + e.ToString());
                logger.Log(e.StackTrace);
            }
        }

        public static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (socket != null && socket.IsBound)
            {
                socket.Close();
                socket = null;
            }

            return true;
        }

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        public static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
            Apply(settings);
        }

        public static void Apply(Settings _settings)
        {
            settings = _settings;

            if (timer != null)
            {
                timer.Enabled = false;
                timer.Stop();
            }

            if (settings.Active)
            {
                logger.Log("Creating timer for " + settings.TimeoutMilliseconds.ToString() + " ms...");
                timer = new Timer(settings.TimeoutMilliseconds);
                timer.Enabled = true;
                timer.AutoReset = true;
                timer.Elapsed += SendUpdate;
                timer.Start();
            }
        }

    }

    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Enable", DrawType.Toggle)] public bool Active = false;

        [Draw("UDP endpoint host", DrawType.Field)] public string UdpHost = "localhost";
        [Draw("UDP endpoint port", DrawType.Field)] public int UdpPort = 10000;

        [Draw("Update interval (ms)", DrawType.Field)] public int TimeoutMilliseconds = 10000;
        [Draw("Debug Timer", DrawType.Toggle)] public bool DebugTimer = true;

        [Draw("Vehicle type", DrawType.Toggle)] public bool SendCarType = true;
        [Draw("Vehicle position", DrawType.Toggle)] public bool SendPosition = true;
        [Draw("Vehicle speed", DrawType.Toggle)] public bool SendSpeed = true;
        [Draw("Train weight (tonnage)", DrawType.Toggle)] public bool SendWeight = true;
        [Draw("Engine RPM", DrawType.Toggle)] public bool SendRPM = true;
        [Draw("Traction force", DrawType.Toggle)] public bool SendForce = false;
        [Draw("Throttle level", DrawType.Toggle)] public bool SendThrottle = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange()
        {
        }
    }
}

