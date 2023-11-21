/**
 * Copyright (C) 2023 Xibo Signage Ltd
 *
 * Xibo - Digital Signage - https://xibosignage.com
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
using Flurl.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace XiboClient.Integration.Nexmosphere
{
    internal class Nexmosphere
    {
        private static readonly Lazy<Nexmosphere>
            lazy =
            new Lazy<Nexmosphere>
            (() => new Nexmosphere());

        public static Nexmosphere Instance
            => lazy.Value;

        private Nexmosphere()
        {
            // Check for a configuration file
            _configFilePath = Path.Combine(ApplicationSettings.Default.LibraryPath, "nexmosphere.json");
            if (!File.Exists(_configFilePath))
            {
                Settings = new Settings
                {
                    IsEnabled = false,
                    ModifiedDt = DateTime.Now,
                };
            }
            else
            {
                // Parse the file
                Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(_configFilePath));
            }
        }

        private SerialPort _port;

        private string _configFilePath;
        private string _buffer = "";

        public event OnMessageReceivedDelegate OnMessageRecieved;
        public delegate void OnMessageReceivedDelegate(Message message);

        public Settings Settings { get; set; }

        public void SaveConfiguration()
        {
            using (FileStream file = new FileStream(_configFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                using (StreamWriter sw = new StreamWriter(file))
                {
                    using (JsonWriter writer = new JsonTextWriter(sw))
                    {
                        writer.Formatting = Formatting.Indented;
                        writer.WriteStartObject();
                        writer.WritePropertyName("IsEnabled");
                        writer.WriteValue(Settings.IsEnabled.ToString());
                        writer.WritePropertyName("PortName");
                        writer.WriteValue(Settings.PortName);
                        writer.WritePropertyName("ModifiedDt");
                        writer.WriteValue(Settings.ModifiedDt.ToInvariantString());
                        writer.WritePropertyName("IsTriggerWebhook");
                        writer.WriteValue(Settings.IsEnabled.ToString());
                        writer.WriteEndObject();
                    }
                }
            }
        }

        public void Start()
        {
            OpenSerialPort();

            _port.DataReceived += Port_DataReceived;
            _port.ErrorReceived += Port_ErrorReceived;
        }

        public void Stop()
        {
            CloseSerialPort();
        }

        public bool SendCommand(string command)
        {
            if (_port == null || !_port.IsOpen)
            {
                return false;
            }

            _port.WriteLine(command);

            return true;
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Parse the event and raise the tag into the criteria manager.
            SerialPort sp = (SerialPort)sender;
            _buffer += sp.ReadExisting();

            // We won't get whole lines, so we have to keep reading until we get the EOL signal
            int eol = _buffer.IndexOf("\r\n");
            while (eol != -1)
            {
                // Take the buffer we have, up to the EOL we've found
                OnMessageRecieved?.Invoke(Message.FromString(_buffer.Substring(0, eol)));

                // Now remove that from the buffer.
                _buffer = _buffer.Remove(0, eol + 2);

                // See if we have another.
                eol = _buffer.IndexOf("\r\n");
            }
        }

        private void Port_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            OnMessageRecieved?.Invoke(new Message
            {
                DateTime = DateTime.Now,
                IsValid = false,
                Raw = e.EventType.ToString(),
            });
        }

        private void OpenSerialPort()
        {
            if (_port == null)
            {
                _port = new SerialPort();
            }

            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.PortName = Settings.PortName ?? "COM1";
            _port.BaudRate = 115200;
            _port.DataBits = 8;
            _port.Parity = Parity.None;
            _port.StopBits = StopBits.One;
            _port.Handshake = Handshake.None;
            _port.NewLine = "\r\n";


            if (!_port.IsOpen)
            {
                try
                {
                    _port.Open();
                }
                catch (Exception ex)
                {
                    OnMessageRecieved?.Invoke(new Message
                    {
                        DateTime = DateTime.Now,
                        IsValid = false,
                        Raw = ex.Message,
                    });
                }
            }
        }

        private void CloseSerialPort()
        {
            if (_port != null && _port.IsOpen)
            {
                _port.Close();
                _port.DataReceived -= Port_DataReceived;
                _port.ErrorReceived -= Port_ErrorReceived;
                _port.Dispose();
                _port = null;
            }
        }
    }
}
