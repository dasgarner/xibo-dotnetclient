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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XiboClient.Action;

namespace XiboClient.Integration.Nexmosphere
{
    internal class Message
    {
        /// <summary>
        /// Nexmosphere command structure
        /// Ref: https://nexmosphere.com/document/API_Manual_Q3_2022.pdf
        /// </summary>
        private static readonly Regex _regex = new Regex(@"^(X|G|S|D)([0-9]{3})(A|B|S)\[(.*)\]$");
        private static readonly Regex _rfIdRegex = new Regex(@"^(XR)\[(.*)\]$");

        public DateTime DateTime { get; set; }
        public string Type { get; set; }
        public string Address { get; set; }
        public string Format { get; set; }
        public string Command { get; set;  }
        public string WebHookTriggerCode
        {
            get
            {
                return Type + "|" + Address + "|" + Format + "|" + Command;
            }
        }
        public string Raw { get; set; }

        public bool IsValid { get; set; }

        /// <summary>
        /// Parse out a Nexmosphere message from the string provided.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static Message FromString(string text)
        {
            Message message = new Message
            {
                DateTime = DateTime.Now,
                IsValid = false,
                Raw = text,
            };

            if (_regex.IsMatch(text))
            {
                // Create a command from the full text.
                MatchCollection matches = _regex.Matches(text);

                GroupCollection groups = matches[0].Groups;
                List<string> parts = new List<string>();
                for (int i = 1; i < groups.Count; i++)
                {
                    parts.Add(groups[i].Value);
                }

                message.Type = parts[0];
                message.Address = parts[1];
                message.Format = parts[2];
                message.Command = parts[3];
                
                // Valid message
                message.IsValid = true;
            } 
            else if (_rfIdRegex.IsMatch(text))
            {
                // RFID sensor has a different format.
                MatchCollection matches = _rfIdRegex.Matches(text);

                GroupCollection groups = matches[0].Groups;
                List<string> parts = new List<string>();
                for (int i = 1; i < groups.Count; i++)
                {
                    parts.Add(groups[i].Value);
                }

                message.Type = parts[0];
                message.Command = parts[1];

                // Valid message
                message.IsValid = true;
            }
            return message;
        }
    }
}
