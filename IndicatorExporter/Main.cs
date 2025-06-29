using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using System.Timers;
using System.Net;
using System.Net.Sockets;
using System.Security.Policy;
using DV.CabControls;
using DV.HUD;
using DV.Utils;

using UnityModManagerNet;

using TinyJson;
using UnityEngine;


namespace IndicatorExporter
{
    [EnableReloading]
    public static class Main
    {
        private static Settings settings;
        private static Socket socket;
        
        private static StreamWriter outputFd;
        private static string outputFdLoco;

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

            outputFdLoco = "";
            
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
                            LogData(loco);
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

        private static void LogData(TrainCar loco)
        {
            Dictionary<String, String> data = new Dictionary<String, String>();

            populateGenerics(data, loco);
            populateDiesel(data, loco);
            populateSteam(data, loco);

            string json = data.ToJson();

            if (settings.DebugTimer)
                Main.logger.Log("Sending data: " + json);

            if (settings.SendUDPPacket)
            {
                SendPacket(json);
            }

            if (settings.WriteToFile)
            {
                LogToFile(data, json);
            }
        }

        private static void SendPacket(string json)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                socket.SendTo(buffer, new IPEndPoint(IPAddress.Parse(settings.UdpHost), settings.UdpPort));
            }
            catch (Exception e)
            {
                logger.Error("Could not send packet: " + e.ToString());
                logger.Log(e.StackTrace);
            }
        }

        private static void LogToFile(Dictionary<String, String> data, string json)
        {
            if (data["vehicle"] != outputFdLoco)
            {
                if(outputFd != null)
                    outputFd.Close();

                outputFdLoco = data["vehicle"];
            }

            if (outputFd == null)
            {
                outputFd = new StreamWriter(GetLocoFilePath(data["plate"] + "_" + data["vehicle"]), append: true);
            }

            outputFd.Write(json + "\n");
        }

        private static string GetLocoFilePath(string name)
        {
            return settings.Directory + "\\" + name;
        }

        private static void populateGenerics(Dictionary<string, string> data, TrainCar loco)
        {
            data.Add("vehicle", loco.CarGUID);

            if(settings.SendForce && loco.SimController != null)
                data.Add("force", loco.SimController.drivingForce.generatedForce.ToString("G"));

            if(settings.SendSpeed)
                data.Add("speed", loco.GetForwardSpeed().ToString("G"));

            if (settings.SendWeight)
            {
                float weight = 0.0f;
                foreach (TrainCar car in loco.trainset.cars)
                {
                    weight += car.massController.TotalMass;
                }
                data.Add("weight", weight.ToString("G"));
            }

            if(settings.SendCarType)
                data.Add("plate", loco.GetComponent<TrainCarPlatesController>().carTypeText);

            if(settings.SendPosition)
                data.Add("position", (loco.transform.position - WorldMover.currentMove).ToString("G"));

            if (settings.SendGrade)
            {
                Vector3 bogie1 = loco.FrontBogie.transform.position;
                Vector3 bogie2 = loco.RearBogie.transform.position;
                float heightDiff = bogie1.y - bogie2.y;
                float bogieDistance = (bogie1 - bogie2).magnitude;
                float grade = heightDiff / bogieDistance;
                data.Add("grade", grade.ToString("G"));
            }

            if (settings.SendGradeAvg)
            {
                float mass = 0.0f;
                float weightedGrade = 0.0f;

                foreach (TrainCar car in loco.trainset.cars)
                {
                    Vector3 bogie1 = car.FrontBogie.transform.position;
                    Vector3 bogie2 = car.RearBogie.transform.position;
                    float heightDiff = bogie1.y - bogie2.y;
                    float bogieDistance = (bogie1 - bogie2).magnitude;
                    float grade = heightDiff / bogieDistance;
                    mass += car.massController.TotalMass;
                    weightedGrade += car.massController.TotalMass * grade;
                }

                data.Add("train_grade", (weightedGrade / mass).ToString("G"));
            }

            if(settings.SendThrottle && loco.SimController != null)
                data.Add("throttle", loco.SimController.controlsOverrider.Throttle.Value.ToString("G"));
        }

        private static void populateDiesel(Dictionary<String, String> data, TrainCar loco)
        {
            if (loco.loadedInterior != null && loco.loadedInterior.GetComponent<InteriorControlsManager>() != null)
            {
                InteriorControlsManager ctls = loco.loadedInterior.GetComponent<InteriorControlsManager>();

                if (ctls.indicatorReader != null)
                {
                    if (settings.SendRPM && ctls.indicatorReader.engineRpm != null)
                        data.Add("rpm", ctls.indicatorReader.engineRpm.Value.ToString("G"));

                    if (settings.SendDrivetrainRPM && ctls.indicatorReader.turbineRpmMeter != null)
                        data.Add("drive_rpm", ctls.indicatorReader.turbineRpmMeter.Value.ToString("G"));

                    if (settings.SendGearState)
                    {
                        ControlImplBase gearboxA = null;
                        ctls.GetComponent<LocoControlsReader>()?.gearboxA?.TryGetComponent<ControlImplBase>(out gearboxA);
                        if (gearboxA != null)
                        {
                            data.Add("gear1", gearboxA.Value.ToString("G"));
                        }

                        ControlImplBase gearboxB = null;
                        ctls.GetComponent<LocoControlsReader>()?.gearboxB?.TryGetComponent<ControlImplBase>(out gearboxB);
                        if (gearboxB != null)
                        {
                            data.Add("gear2", gearboxB.Value.ToString("G"));
                        }
                    }
                }
            }
        }

        private static void populateSteam(Dictionary<String, String> data, TrainCar loco)
        {
            if(settings.SendReverser
               && loco.SimController?.controlsOverrider.Reverser != null)
                data.Add("reverser", loco.SimController.controlsOverrider.Reverser.Value.ToString("G"));

            if(settings.SendPressureBoiler
               && loco.loadedInterior?.GetComponent<InteriorControlsManager>()?.indicatorReader.steam != null)
                data.Add("boiler_p", loco.loadedInterior.GetComponent<InteriorControlsManager>().indicatorReader.steam.Value.ToString("G"));

            if(settings.SendPressureChest
               && loco.loadedInterior?.GetComponent<InteriorControlsManager>()?.indicatorReader.steamChest != null)
                data.Add("chest_p", loco.loadedInterior.GetComponent<InteriorControlsManager>().indicatorReader.steamChest.Value.ToString("G"));
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
        [Header("Sampling rate")]
        [Draw("Enable sampling", DrawType.Toggle)] public bool Active = false;
        [Draw("Update interval (ms)", DrawType.Field)] public int TimeoutMilliseconds = 100;
        [Draw("Debug (verbose)", DrawType.Toggle)] public bool DebugTimer = false;

        [Header("Output to file")]
        [Draw("Write to local file", DrawType.Toggle)] public bool WriteToFile = true;
        [Draw("Folder to put logs in", DrawType.Field)] public string Directory = "C:\\";

        [Header("Network settings")]
        [Draw("Enable UDP client", DrawType.Toggle)] public bool SendUDPPacket = false;
        [Draw("UDP endpoint host", DrawType.Field)] public string UdpHost = "localhost";
        [Draw("UDP endpoint port", DrawType.Field)] public int UdpPort = 10000;

        [Header("All locomotives")]
        [Draw("Vehicle type", DrawType.Toggle)] public bool SendCarType = true;
        [Draw("Vehicle position", DrawType.Toggle)] public bool SendPosition = true;
        [Draw("Track grade", DrawType.Toggle)] public bool SendGrade = false;
        [Draw("Train-averaged track grade", DrawType.Toggle)] public bool SendGradeAvg = false;
        [Draw("Vehicle speed", DrawType.Toggle)] public bool SendSpeed = true;
        [Draw("Train weight (tonnage)", DrawType.Toggle)] public bool SendWeight = true;
        [Draw("Throttle level", DrawType.Toggle)] public bool SendThrottle = false;
        [Draw("Traction force", DrawType.Toggle)] public bool SendForce = false;

        [Header("Diesel engines")]
        [Draw("Engine RPM", DrawType.Toggle)] public bool SendRPM = true;
        [Draw("Drivetrain RPM", DrawType.Toggle)] public bool SendDrivetrainRPM = true;
        // TODO: gearbox controls are hard to pinpoint 
        // [Draw("Gearbox setting", DrawType.Toggle)]
        public bool SendGearState = false;

        [Header("Steam engines")]
        [Draw("Reverser", DrawType.Toggle)] public bool SendReverser = false;
        [Draw("Boiler pressure", DrawType.Toggle)] public bool SendPressureBoiler = false;
        [Draw("Chest pressure", DrawType.Toggle)] public bool SendPressureChest = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public void OnChange()
        {
        }
    }
}

