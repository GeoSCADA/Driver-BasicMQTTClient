using System;
using System.Collections.Generic;
using System.Text;
using ClearSCADA.DBObjFramework;
using ClearSCADA.DriverFramework;
using MQTTClient;
using uPLibrary.Networking.M2Mqtt;
using System.Timers;
using System.Security.Cryptography.X509Certificates;

using System.Diagnostics;


namespace DriverMQTTClient
{
	class DriverMQTTClient
	{
		static void Main(string[] args)
		{
			// Debugger.Launch();

			using (DriverApp App = new DriverApp())
			{
				// Init the driver with the database module
				if (App.Init(new CSharpModule(), args))
				{
					// Do custom driver init here
					App.Log("MQTT Client driver started.");

					// Start the driver MainLoop
					App.MainLoop();
				}
			}
		}
	}

    public class DrvCSScanner : DriverScanner<MQTTBroker>
    {
        // The Mqtt Library object
        private MqttClient client;

        // The Reconnection Timer
        private Timer reconTimer;

        private bool connectInProgress;
        private bool connectFail;

        public override SourceStatus OnDefine()
        {
            // Create reconnect timer - a 10 second timer used to reconnect if the broker connection fails
            // There is a timer object for Channels, but not for Scanners.
            reconTimer = new Timer(10000);
            reconTimer.Enabled = false;
            reconTimer.AutoReset = false;
            reconTimer.Elapsed += HandleTimer;

            // MQTT does not scan, but setting rate as a default in case this is expected.
            SetScanRate( (uint) DBScanner["ScanRate"]);

            Log("Scanner defined.");

            return doConnect();
        }

        private SourceStatus doConnect()
        {
            Object Reply = null;
            Object Data = "Connecting...";
            App.SendReceiveObject(DBScanner.Id, OPCProperty.Base + 4, Data, ref Reply);

            Log("Attempting connection.");
            string errorText;
            if (connectTo((string)DBScanner.BrokerHost, DBScanner.BrokerPort, DBScanner.Username, DBScanner.Password, 
                            DBScanner.MQTTVersion, DBScanner.ClientId, DBScanner.Security,
                            DBScanner.caCertFile, DBScanner.clientCertFile, out errorText))
            {
                Log("Connected.");

                Data = "Connected.";
                App.SendReceiveObject(DBScanner.Id, OPCProperty.Base + 5, Data, ref Reply);
                return SourceStatus.Online;
            }
            else
            {
                Log("Not Connected.");

                Data = "Not Connected. " + errorText;
                App.SendReceiveObject(DBScanner.Id, OPCProperty.Base + 6, Data, ref Reply);
                ScheduleBrokerConnect();

                return SourceStatus.Offline;
            }

        }

        private Dictionary<string, PointSourceEntry> TopicLookup = new Dictionary<string, PointSourceEntry>();

        private void ScheduleBrokerConnect()
        {
            reconTimer.Enabled = true;
        }
        private void HandleTimer(Object source, ElapsedEventArgs e)
        {
            reconTimer.Enabled = false;
            if (client.IsConnected)
            {
                Log("HandleTimer - Already connected, ignore.");
                return;
            }
            Log("HandleTimer - call doConnect");
            SourceStatus s = doConnect();
            if (s != SourceStatus.Online)
            {
                Log("HandleTimer - Failed doConnect");
                SetFailReason("Timed retry failed to connect.");
            }
            else
            {
                Log("HandleTimer - Connected OK");
                // Set status back online
                SetStatus(SourceStatus.Online);
            }
        }

        private bool connectTo(string hostname, UInt16 portnumber, string username, string password, 
                                byte Version, string clientId, byte security, string caCertFile, string clientCertFile, out string errorText)
        {
            try
            {
                // create client instance
                if (security == 0)
                {
                    client = new MqttClient(hostname, portnumber, false, MqttSslProtocols.None, null, null);
                }
                else
                {
                    X509Certificate caCert;
                    X509Certificate clientCert;
                    try
                    {
                        caCert = new X509Certificate(caCertFile);
                    } catch (Exception e)
                    {
                        Log("Error reading or creating certificate from CA Certificate file: " + e.Message);
                        throw e;
                    }
                    try
                    {
                        clientCert = new X509Certificate(clientCertFile);
                    }
                    catch (Exception e)
                    {
                        Log("Error reading or creating certificate from Client Certificate file: " + e.Message);
                        throw e;
                    }
                    client = new MqttClient(hostname, portnumber, true, caCert, clientCert, (MqttSslProtocols)security);
                }

                if (Version == 0)
                {
                    Log("Protocol version 3.1");
                    client.ProtocolVersion = MqttProtocolVersion.Version_3_1;
                }
                else if (Version == 1)
                {
                    Log("Protocol version 3.1.1");
                    client.ProtocolVersion = MqttProtocolVersion.Version_3_1_1;
                }
                else
                {
                    Log("Unknown protocol version");
                }
            }
            catch (Exception Fault)
            {
                Log("Exception (Create client): " + Fault.Message);
                errorText = Fault.Message;
                return false;
            }

            // register to message received 
            client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;
            client.ConnectionClosed += client_MqttConnectionClosed;

            //string clientId = Guid.NewGuid().ToString();
            try
            {
                // Guard connection process so if callback for disconnection is called we will know
                connectInProgress = true;
                connectFail = false;

                if (username == null || username == "")
                {
                    // create client instance 
                    Log("Connecting with no user");
                    client.Connect(clientId);
                }
                else
                {
                    // create client instance with login.
                    Log("Connecting with user: " + username + ", " + password);
                    client.Connect(clientId, username, password);
                }
                Log("Connected to MQTT server: " + hostname);

                connectInProgress = false;
            }
            catch (Exception Fault)
            {
                connectInProgress = false;
                Log("Exception (Connect client): " + Fault.Message);
                errorText = Fault.Message;
                return false;
            }
            if (connectFail)
            {
                Log("Failed connection during connectTo");
                errorText = "Failed connection during connectTo";
                return false;
            }

            // Subscribe to all
            foreach (PointSourceEntry Entry in Points)
            {
                // Could be batched and subscribed as one array, perhaps batch up to 10 with Array.Resize( ref array, n) on last batch?

                SimplePoint p = (SimplePoint)Entry.DatabaseObject;
                string topic = (string)p["Topic"];
                byte q = (byte)p["QOSType"];
                //Cache topics and references to points
                try
                {
                    TopicLookup.Add(topic, Entry);
                    // subscribe to the topic 
                    client.Subscribe(new string[] { topic }, new byte[] { q });
                    Log("Subscribed OK to: " + topic);
                }
                catch (Exception Fault)
                {
                    Log("Exception (Add Topic): " + Fault.Message);
                    errorText = "Exception (Add Topic): " + Fault.Message;
                    return false;
                }
            }
            errorText = "";
            return true;
        }

        public override void OnUnDefine()
        {
            Log("Undefine called");
            try
            {
                client.Disconnect();
            }
            catch (Exception Fault)
            {
                Log("Error disconnecting: " + Fault.Message);
            }
            Log("Disconnected from broker");
            // Dereference topics
            TopicLookup.Clear();
        }

        private void client_MqttConnectionClosed(object sender, System.EventArgs e)
        {
            Log("Callback to MqttConnectionClosed: set connectFail");
            connectFail = true;

            // If we're trying a connection right now, then skip
            if (connectInProgress == false)
            {
                this.SetFailReason("Connection Closed");
                // Need to dereference topics and schedule a reconnect
                TopicLookup.Clear();
                Log("Set offline");
                base.SetStatus(SourceStatus.Offline);

                string Data = "Not Connected.";
                object Reply = null;
                App.SendReceiveObject(DBScanner.Id, OPCProperty.Base + 6, Data, ref Reply);
            }
            else
            {
                Log("Connect in progress.");
            }
            Log("Schedule a reconnection");
            ScheduleBrokerConnect();
        }

        private void client_MqttMsgPublishReceived(object sender, uPLibrary.Networking.M2Mqtt.Messages.MqttMsgPublishEventArgs e)
        {
            // handle message received 
            String s = "";
            foreach (byte b in e.Message)
            {
                s += (char)b;
            }
            Log("Received from: " + sender.ToString() + " Message: " + s);
            // Find the point entry with this topic
            string t = e.Topic;
			Log("Topic was: " + t);

			// Standard input of topic and data
			if (TopicLookup.ContainsKey(t))
            {
                PointSourceEntry p = TopicLookup[t];
                PointSourceEntry.Quality Qual = PointSourceEntry.Quality.Good;
				if (p.PointType == typeof(MQTTPointDg))
				{
					int Val = Convert.ToByte(s);
					if (p.ValueChanged(Qual, Val))
						p.SetValue(Qual, Val);
				}
				else if (p.PointType == typeof(MQTTPointAg))
				{
					Double Val = Convert.ToDouble(s);
					if (p.ValueChanged(Qual, Val))
						p.SetValue(Qual, Val);
				}
				else if (p.PointType == typeof(MQTTPointString))
				{
					if (p.ValueChanged(Qual, s))
						p.SetValue(Qual, s);
				}
				else if (p.PointType == typeof(MQTTPointTime))
				{
					DateTime Val = Convert.ToDateTime(s);
					if (p.ValueChanged(Qual, Val))
						p.SetValue(Qual, Val);
				}
				Log("Updated: " + p.FullName);
                // Refresh
                this.FlushUpdates();
            }
        }

        // Left blank - there is no scanning for MQTT
        public override void OnScan()
        {
            Log("OnScan called - no action.");
		}

        public override void OnControl(PointSourceEntry Point, object Value)
        {
            if (Point.PointType.Name == "MQTTPointDg") 
			{
                ControlDigital(Point, Value);
            }
            else if (Point.PointType.Name == "MQTTPointAg") 
			{
                ControlAnalogue(Point, Value);
            }
			else if (Point.PointType.Name == "MQTTPointString") 
			{
				ControlString(Point, Value);
			}
			else
			{
                throw new Exception("No handler found for " + Point.FullName);
            }
        }

        private void ControlDigital(PointSourceEntry entry, object val)
        {
            Log("Control Digital");
            byte value = (byte)val;
            MQTTPointDg point = (MQTTPointDg)(entry.DatabaseObject);
            // Use ControlTopic, else if blank, publish back to the same topic with the same QOS
            string ct = point.Topic;
            if (point.ControlTopic != "")
            {
                ct = point.ControlTopic;
            }
            client.Publish(ct, Encoding.UTF8.GetBytes(val.ToString()), point.QOSType, false); 

        }
        private void ControlAnalogue(PointSourceEntry entry, object val)
        {
            Log("Control Analogue");
            double value = (double)val;
            MQTTPointAg point = (MQTTPointAg)(entry.DatabaseObject);
            // Use ControlTopic, else if blank, publish back to the same topic with the same QOS
            string ct = point.Topic;
            if (point.ControlTopic != "")
            {
                ct = point.ControlTopic;
            }
            client.Publish(ct, Encoding.UTF8.GetBytes(val.ToString()), point.QOSType, false); 

        }
		private void ControlString(PointSourceEntry entry, object val)
		{
            Log("Control String");
            string value = (string)val;
			MQTTPointString point = (MQTTPointString)(entry.DatabaseObject);
			// Use ControlTopic, else if blank, publish back to the same topic with the same QOS
			string ct = point.Topic;
			if (point.ControlTopic != "")
			{
				ct = point.ControlTopic;
			}
			client.Publish(ct, Encoding.UTF8.GetBytes(val.ToString()), point.QOSType, false);

		}
	}
}
