/**
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XiboClient.Rendering
{
    abstract class VideoMedia : Media
    {
        protected string _filePath;
        protected int _duration;
        protected int volume;
        protected bool _detectEnd = false;
        protected bool isLooping = false;
        protected readonly bool isFullScreenRequest = false;
        protected bool ShouldBeVisible { get; set; }
        protected bool Muted { get; set; }
        protected bool Stretch { get; set; }

        /// <summary>
        /// Should we seek to a position or not
        /// </summary>
        protected double _position;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public VideoMedia(RegionOptions options) : base(options)
        {
            this.ShouldBeVisible = true;

            _filePath = Uri.UnescapeDataString(options.uri).Replace('+', ' ');
            _duration = options.duration;

            // Handle Volume
            this.volume = options.Dictionary.Get("volume", 100);

            // Mute - if not provided as an option, we keep the default.
            string muteOption = options.Dictionary.Get("mute");
            if (!string.IsNullOrEmpty(muteOption))
            {
                this.Muted = muteOption == "1";
            }

            // Should we loop?
            this.isLooping = (options.Dictionary.Get("loop", "0") == "1" && _duration != 0);

            // Full Screen?
            this.isFullScreenRequest = options.Dictionary.Get("showFullScreen", "0") == "1";

            // Scale type
            Stretch = options.Dictionary.Get("scaleType", "aspect").ToLowerInvariant() == "stretch";
        }

        /// <summary>
        /// Override the timer tick
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected override void timer_Tick(object sender, EventArgs e)
        {
            if (!_detectEnd || Expired)
            {
                // We're not end detect, so we pass the timer through
                base.timer_Tick(sender, e);
            }
        }

        /// <summary>
        /// Is a region size change required
        /// </summary>
        /// <returns></returns>
        public override bool RegionSizeChangeRequired()
        {
            return this.isFullScreenRequest;
        }

        /// <summary>
        /// Get the configured video engine
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        public static VideoMedia GetConfiguredVideoMedia(RegionOptions options)
        {
            VideoMedia media;

            if (ApplicationSettings.Default.FfmpegAvailable && ApplicationSettings.Default.UseFFmpeg)
            {
                media = new VideoFFmpeg(options);
            }
            else
            {
                media = new Video(options);
            }

            return media;
        }
    }
}
