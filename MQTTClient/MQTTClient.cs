using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.Remoting.Messaging;

using ClearSCADA.DBObjFramework;
using ClearSCADA.DriverFramework;

[assembly:Category("MQTT Client")]
[assembly:Description("MQTT Client Driver")]
[assembly:DriverTask("DriverMQTTClient.exe")]

[System.ComponentModel.RunInstaller(true)]
public class CSInstaller :  DriverInstaller
{
}

namespace MQTTClient
{
    public class CSharpModule : DBModule
    {
    }

    [Table("MQTT Broker", "MQTT Broker")]
    [EventCategory("MQTTBroker", "MQTT Broker", OPCProperty.Base + 9)]
    public class MQTTBroker : Scanner
    {
        [AlarmCondition("MQTTScannerBrokerAlarm", "MQTTBroker", 0x0350532F)]
        [AlarmSubCondition("MQTTScannerCommError")]
        public Alarm MQTTScannerAlarm;

        [Label("In Service", 1, 1)]
        [ConfigField("In Service", 
                     "Controls whether broker connection is active.",
                     1, 2, 0x350501B, DefaultOverride = true)]
        public override Boolean InService
        {
            get 
            { 
                return base.InService;
            }
            set 
            { 
                base.InService = value;
            }
        }

        [Label("Severity", 2, 1)]
        [ConfigField("Severity", "Severity used for alarm and event logging.", 2, 2, 0x0350501C)]
        public override ushort Severity
        {
            get
            {
                return base.Severity;
            }
            set
            {
                base.Severity = value;
            }
        }


        [Label("Area of Interest", 3, 1, AreaOfInterest = true)]
        [ConfigField("AOI Ref", "A reference to an AOI.", 3, 2, 0x0465700E)]
        public AOIReference AOIRef;

        [ConfigField("AOI Name", "A reference to an AOI.", 3, 3, 0x0465700F,
                     ReadOnly = true, Length = 48, Flags = FormFlags.Hidden)]
        public String AOIName
        {
            get { return AOIRef.Name; }
        }


        [Label("Broker Host", 4, 1)]
        [ConfigField("BrokerHost",
                     "The IP address or network name of the MQTT broker.",
                     4, 2, OPCProperty.Base + 1, Length = 80)]
        public string BrokerHost;

        [Label("Broker Port", 4, 3)]
        [ConfigField("BrokerPort",
                     "The IP port of the MQTT broker. (Default 1883).",
                     4, 4, OPCProperty.Base + 3)]
        public UInt16 BrokerPort = 1883; // Default. SSL Default is 8883

        [Label("Username", 5, 1)]
        [ConfigField("Username",
                     "The user name configured for login to the MQTT broker. Leave blank if unused.",
                     5, 2, OPCProperty.Base + 7, Length = 80)]
        public string Username;

        [Label("Password", 5, 3)]
        [ConfigField("Password",
                     "The password configured for login to the MQTT broker.",
                     5, 4, OPCProperty.Base + 8, Length = 300, Flags = FormFlags.Password)]
        public string Password;

        [Label("Version", 6, 1)]
        [ConfigField("MQTTVersion",
                        "The version of the MQTT connection.",
                        6, 2, OPCProperty.Base + 10)]
        [Enum(new String[] { "3.1", "3.1.1" })]
        public Byte MQTTVersion = 1;

        [Label("Client Id", 6, 3)]
        [ConfigField("ClientId",
                     "The client identification string for connection to the MQTT broker.",
                     6, 4, OPCProperty.Base + 11, Length = 80)]
        public string ClientId = Guid.NewGuid().ToString();

        [Label("Security", 7, 1)]
        [ConfigField("Security",
                        "The type of encryption to be used for the MQTT connection.",
                        7, 2, OPCProperty.Base + 12)]
        [Enum(new String[] { "None", "SSL v3", "TLS v1.0", "TLS v1.1", "TLS v1.2" })]
        public Byte Security = 0;

        [Label("CA Certificate Filename", 8, 1)]
        [ConfigField("caCertFile",
                     "CA X509 Certificate File Name.",
                     8, 2, OPCProperty.Base + 13, Length = 120)]
        public string caCertFile;

        [Label("Client Certificate Filename", 8, 3)]
        [ConfigField("clientCertFile",
                     "Client X509 Certificate File Name.",
                     8, 4, OPCProperty.Base + 14, Length = 120)]
        public string clientCertFile;

        [Label("X509 Certificates in DER format.", 8, 5)]

        // Not needed as MQTT does not scan.
        // Hidden: [Label("Scan Rate", 5, 1)]
        [ConfigField("Scan Rate",
                     "The rate of scanning the remote host. (Unused).",
                     5, 2, 0x03505045, Flags = FormFlags.Hidden)]
        [Interval(IntervalType.Milliseconds)]
        public UInt32 ScanRate = 10000;

        [DataField("Last Error",
                   "The text of the last error.",
                   OPCProperty.Base + 2)]
        public String ErrMessage;       

        public override void OnValidateConfig(MessageInfo Errors)
        {
			// Check node not empty
            if ((BrokerHost == null) || (BrokerHost.Length == 0))
            {
                Errors.Add(this, "BrokerHost", "Broker Host name/address is empty.");
            }
            if (BrokerPort <= 0)
            {
                Errors.Add(this, "BrokerPort", "Broker Port is zero.");
            }
            base.OnValidateConfig(Errors);
        }

        public override void OnReceive(uint Type, object Data, ref object Reply)
        {
            // Update error string
            if (Type == OPCProperty.Base + 4)
            {
                ErrMessage = (string)Data;
            }
            // Clear Scanner Alarm
            else if (Type == OPCProperty.Base + 5)
            {
                ErrMessage = (string)Data;
                if (MQTTScannerAlarm.ActiveSubCondition == "MQTTScannerCommError")
                {
                    MQTTScannerAlarm.Clear();
                    SetDataModified(true);
                }
            }
            // Set Scanner Alarm
            else if (Type == OPCProperty.Base + 6)
            {
                ErrMessage = (string)Data;
                if (MQTTScannerAlarm.ActiveSubCondition != "MQTTScannerCommError")
                {
                    MQTTScannerAlarm.Raise("MQTTScannerCommError", "MQTT Error: Offline, Alarm Active." + 
                                            ErrMessage.Substring(0, ErrMessage.Length > 200 ? 200 : ErrMessage.Length), 
                                            Severity, true);
                    SetDataModified(true);
                }
            }
            else
                base.OnReceive(Type, Data, ref Reply);
        }
	}

    [Table("MQTT Analogue", "MQTT")]
    public class MQTTPointAg : AnaloguePoint
    {
        [Label("Broker", 1, 1)]
        [ConfigField("Scanner", "Scanner", 1, 2, 0x0350532F)]
        public new Reference<MQTTBroker> ScannerId
        {
            get
            {
                return new Reference<MQTTBroker>(base.ScannerId);
            }
            set
            {
                base.ScannerId = new Reference<Scanner>(value);
            }
        }

        [Label("Topic", 2, 1)]
        [ConfigField("Topic",
                     "The topic name of the source of data.",
                     2, 2, OPCProperty.Base + 0x31, Length = 80)]
        public string Topic;

        [Label("QOSType", 3, 1)]
        [ConfigField("QOSType",
                        "The quality of service for the MQTT subscription.",
                        3, 2, OPCProperty.Base + 0x32)]
        [Enum(new String[] { "At Most Once", "At Least Once", "Exactly Once" })] //,"Granted Failure"?
        public Byte QOSType;

        [Aggregate("Enabled", "Control", 0x03600000, "CCtrlAlg")]
        public Aggregate Control;

        [Label("ControlTopic", 5, 1)]
        [ConfigField("ControlTopic",
                     "The topic name used for sending controls. If blank, will use the incoming topic name.",
                     5, 2, OPCProperty.Base + 0x33, Length = 80)]
        public string ControlTopic;

        public override void OnConfigChange(ConfigEvent Event, MessageInfo Errors, DBObject OldObject)
		{
			base.OnConfigChange(Event, Errors, OldObject);
		}

        public override void OnValidateConfig(MessageInfo Errors)
        {
            if (Topic == null || Topic.Length == 0)
            {
                Errors.Add(this, "Topic", "Topic name should not be blank.");
            }
            // Check for duplicate topic names
            foreach(SimplePoint p in ScannerId.Value.Points)
            {
                string ptopic = utils.getTopicFromPoint(p);
                if (Topic == ptopic && p.Id != this.Id)
                {
                    // Found a duplicate, error.
                    Errors.Add(this, "Topic", "Duplicate topic name shared by another point.");
                    break;
                }
            }

            //if (ControlTopic == null || ControlTopic.Length == 0)
            //{
            //    Errors.Add(this, "ControlTopic", "Control topic name should not be blank.");
            //}
            base.OnValidateConfig(Errors);
        }

    }

	[Table("MQTT Digital", "MQTT")]
	public class MQTTPointDg : DigitalPoint
	{
		[Label("Broker", 1, 1)]
		[ConfigField("Scanner", "Scanner", 1, 2, 0x0350532F)]
        public new Reference<MQTTBroker> ScannerId
		{
			get
			{
                return new Reference<MQTTBroker>(base.ScannerId);
			}
			set
			{
				base.ScannerId = new Reference<Scanner>(value);
			}
		}

        [Label("Topic", 2, 1)]
        [ConfigField("Topic",
                     "The topic name of the source of data",
                     2, 2, OPCProperty.Base + 0x31, Length = 80)]
        public string Topic;

        [Label("QOSType", 3, 1)]
        [ConfigField("QOSType",
                        "The quality of service for the MQTT subscription",
                        3, 2, OPCProperty.Base + 0x32)]
        [Enum(new String[] { "At Most Once", "At Least Once", "Exactly Once" })] //,"Granted Failure"?
        public Byte QOSType;

        [Aggregate("Enabled", "Control", 0x03600000, "CCtrlDigital")]
        public Aggregate Control;

        [Label("ControlTopic", 5, 1)]
        [ConfigField("ControlTopic",
                     "The topic name used for sending controls. If blank, will use the incoming topic name.",
                     5, 2, OPCProperty.Base + 0x33, Length = 80)]
        public string ControlTopic;

        public override void OnConfigChange(ConfigEvent Event, MessageInfo Errors, DBObject OldObject)
        {
            base.OnConfigChange(Event, Errors, OldObject);
        }

        public override void OnValidateConfig(MessageInfo Errors)
		{
            if (Topic == null || Topic.Length == 0)
            {
                Errors.Add(this, "Topic", "Topic name should not be blank.");
            }
            // Check for duplicate topic names
            foreach (SimplePoint p in ScannerId.Value.Points)
            {
                string ptopic = utils.getTopicFromPoint(p);
                if (Topic == ptopic && p.Id != this.Id)
                {
                    // Found a duplicate, error.
                    Errors.Add(this, "Topic", "Duplicate topic name shared by another point.");
                    break;
                }
            }
            //if (ControlTopic == null || ControlTopic.Length == 0)
            //{
            //    Errors.Add(this, "ControlTopic", "Control topic name should not be blank.");
            //}
            base.OnValidateConfig(Errors);
		}
	}

	[Table("MQTT String", "MQTT")]
	public class MQTTPointString : StringPoint
	{
		[Label("Broker", 1, 1)]
		[ConfigField("Scanner", "Scanner", 1, 2, 0x0350532F)]
		public new Reference<MQTTBroker> ScannerId
		{
			get
			{
				return new Reference<MQTTBroker>(base.ScannerId);
			}
			set
			{
				base.ScannerId = new Reference<Scanner>(value);
			}
		}

		[Label("Topic", 2, 1)]
		[ConfigField("Topic",
					 "The topic name of the source of data",
					 2, 2, OPCProperty.Base + 0x31, Length = 80)]
		public string Topic;

		[Label("QOSType", 3, 1)]
		[ConfigField("QOSType",
						"The quality of service for the MQTT subscription",
						3, 2, OPCProperty.Base + 0x32)]
		[Enum(new String[] { "At Most Once", "At Least Once", "Exactly Once" })] //,"Granted Failure"?
		public Byte QOSType;

		[Aggregate("Enabled", "Control", 0x03600000, "CCtrlString")]
		public Aggregate Control;

		[Label("ControlTopic", 5, 1)]
		[ConfigField("ControlTopic",
					 "The topic name used for sending controls. If blank, will use the incoming topic name.",
					 5, 2, OPCProperty.Base + 0x33, Length = 80)]
		public string ControlTopic;

		public override void OnConfigChange(ConfigEvent Event, MessageInfo Errors, DBObject OldObject)
		{
			base.OnConfigChange(Event, Errors, OldObject);
		}

		public override void OnValidateConfig(MessageInfo Errors)
		{
            if (Topic == null || Topic.Length == 0)
            {
                Errors.Add(this, "Topic", "Topic name should not be blank.");
            }
            // Check for duplicate topic names
            foreach (SimplePoint p in ScannerId.Value.Points)
            {
                string ptopic = utils.getTopicFromPoint(p);
                if (Topic == ptopic && p.Id != this.Id)
                {
                    // Found a duplicate, error.
                    Errors.Add(this, "Topic", "Duplicate topic name shared by another point.");
                    break;
                }
            }
            //if (ControlTopic == null || ControlTopic.Length == 0)
            //{
            //    Errors.Add(this, "ControlTopic", "Control topic name should not be blank.");
            //}
            base.OnValidateConfig(Errors);
		}

	}
	[Table("MQTT Time", "MQTT")]
	public class MQTTPointTime : TimePoint
	{
		[Label("Broker", 1, 1)]
		[ConfigField("Scanner", "Scanner", 1, 2, 0x0350532F)]
		public new Reference<MQTTBroker> ScannerId
		{
			get
			{
				return new Reference<MQTTBroker>(base.ScannerId);
			}
			set
			{
				base.ScannerId = new Reference<Scanner>(value);
			}
		}

		[Label("Topic", 2, 1)]
		[ConfigField("Topic",
					 "The topic name of the source of data",
					 2, 2, OPCProperty.Base + 0x31, Length = 80)]
		public string Topic;

		[Label("QOSType", 3, 1)]
		[ConfigField("QOSType",
						"The quality of service for the MQTT subscription",
						3, 2, OPCProperty.Base + 0x32)]
		[Enum(new String[] { "At Most Once", "At Least Once", "Exactly Once" })] //,"Granted Failure"?
		public Byte QOSType;

		public override void OnConfigChange(ConfigEvent Event, MessageInfo Errors, DBObject OldObject)
		{
			base.OnConfigChange(Event, Errors, OldObject);
		}

		public override void OnValidateConfig(MessageInfo Errors)
		{
            if (Topic == null || Topic.Length == 0)
            {
                Errors.Add(this, "Topic", "Topic name should not be blank.");
            }
            // Check for duplicate topic names
            foreach (SimplePoint p in ScannerId.Value.Points)
            {
                string ptopic = utils.getTopicFromPoint(p);
                if (Topic == ptopic && p.Id != this.Id)
                {
                    // Found a duplicate, error.
                    Errors.Add(this, "Topic", "Duplicate topic name shared by another point.");
                    break;
                }
            }
            base.OnValidateConfig(Errors);
		}
	}
    public class utils
    {
        public static string getTopicFromPoint(SimplePoint p)
        {
            // Find the topic for this point
            string t = p.GetType().ToString();
            switch (t)
            {
                case "MQTTClient.MQTTPointAg":
                    return ((MQTTPointAg)p).Topic;
                case "MQTTClient.MQTTPointDg":
                    return ((MQTTPointDg)p).Topic;
                case "MQTTClient.MQTTPointString":
                    return ((MQTTPointString)p).Topic;
                case "MQTTClient.MQTTPointTime":
                    return ((MQTTPointTime)p).Topic;
                default:
                    return "";
            }
        }

    }
}
