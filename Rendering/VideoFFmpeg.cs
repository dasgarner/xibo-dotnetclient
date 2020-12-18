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
using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using Unosquare.FFME;
using Unosquare.FFME.Common;

namespace XiboClient.Rendering
{
    class VideoFFmpeg : VideoMedia
    {
        /// <summary>
        /// The Media element for Playback
        /// </summary>
        private MediaElement mediaElement;

        /// <summary>
        /// Is this a stream or file?
        /// </summary>
        private bool isStream = false;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public VideoFFmpeg(RegionOptions options) : base(options)
        {
            
        }

        /// <summary>
        /// Media Failed to Load
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaFailed(object sender, MediaFailedEventArgs e)
        {
            // Log and expire
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaFailed: Media Failed. E = " + e.ErrorException.Message), LogType.Error.ToString());

            Expired = true;
        }

        /// <summary>
        /// Media Ended
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaEnded(object sender, EventArgs e)
        {
            // Should we loop?
            if (isLooping)
            {
                this.mediaElement.Position = TimeSpan.Zero;
                this.mediaElement.Play();
            }
            else
            {
                Expired = true;
            }
        }

        /// <summary>
        /// Media is loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                this.mediaElement.Play();
            }
            catch (Exception ex)
            {
                // Problem calling play, we should expire.
                Trace.WriteLine(new LogMessage("Video", "MediaElement_Loaded: Media Failed. E = " + ex.Message), LogType.Error.ToString());
            }
        }

        /// <summary>
        /// Render
        /// </summary>
        /// <param name="position"></param>
        public override void RenderMedia(double position)
        {
            // Save the position
            this._position = position;

            // Check to see if the video exists or not (if it doesnt say we are already expired)
            // we only do this if we aren't a stream
            Uri uri = new Uri(_filePath);

            if (uri.IsFile && !File.Exists(_filePath))
            {
                Trace.WriteLine(new LogMessage("Video", "RenderMedia: " + this.Id + ", File " + _filePath + " not found."));
                throw new FileNotFoundException();
            }

            // Configure whether we are a stream or not.
            this.isStream = !uri.IsFile;

            // Create a Media Element
            this.mediaElement = new MediaElement
            {
                Volume = this.volume,
                IsMuted = this.Muted,
                LoadedBehavior = MediaPlaybackState.Manual
            };

            // This is false if we're an audio module, otherwise video.
            if (!this.ShouldBeVisible)
            {
                this.mediaElement.Width = 0;
                this.mediaElement.Height = 0;
                this.mediaElement.Visibility = Visibility.Hidden;
            }
            else
            {
                // Assert the Width/Height of the Parent
                this.mediaElement.Width = Width;
                this.mediaElement.Height = Height;
                this.mediaElement.Visibility = Visibility.Visible;
            }

            // Handle stretching
            if (Stretch)
            {
                this.mediaElement.Stretch = System.Windows.Media.Stretch.Fill;
            }

            // Events
            this.mediaElement.MediaOpening += MediaElement_MediaOpening;
            this.mediaElement.MediaOpened += MediaElement_MediaOpened;
            this.mediaElement.Loaded += MediaElement_Loaded;
            this.mediaElement.MediaEnded += MediaElement_MediaEnded;
            this.mediaElement.MediaFailed += MediaElement_MediaFailed;
            this.mediaElement.MessageLogged += MediaElement_MediaMessageLogged;

            // Do we need to determine the end time ourselves?
            if (_duration == 0)
            {
                // Set the duration to 1 second
                // this essentially means RenderMedia will set up a timer which ticks every second
                // when we're actually expired and we detect the end, we set expired
                Duration = 1;
                _detectEnd = true;
            }

            // Render media as normal (starts the timer, shows the form, etc)
            base.RenderMedia(position);

            try
            {
                // Start Player
                this.mediaElement.Open(uri);

                this.MediaScene.Children.Add(this.mediaElement);

                Trace.WriteLine(new LogMessage("Video", "RenderMedia: " + this.Id + ", added MediaElement and set source, detect end is " + _detectEnd), LogType.Audit.ToString());
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("Video", "RenderMedia: " + ex.Message), LogType.Error.ToString());

                // Unable to start video - expire this media immediately
                throw;
            }
        }

        /// <summary>
        /// Fired when a new Media item is opening
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaOpening(object sender, MediaOpeningEventArgs e)
        {
            // We only do things on videos
            if (e.Options.VideoStream is StreamInfo videoStream)
            {
                // This might be something we can do if we make CMS modifications to push SRT files down with the video.
                // see: https://github.com/xibosignage/xibo-dotnetclient/issues/163
                // Get the local file path from the URL (if possible)
                /*var mediaFilePath = string.Empty;
                try
                {
                    var url = new Uri(e.Info.MediaSource);
                    mediaFilePath = url.IsFile || url.IsUnc ? Path.GetFullPath(url.LocalPath) : string.Empty;
                }
                catch { *//* Ignore Exceptions *//* }

                // Look for side loadable SRT files
                if (string.IsNullOrWhiteSpace(mediaFilePath) == false)
                {
                    var srtFilePath = Path.ChangeExtension(mediaFilePath, "srt");
                    if (File.Exists(srtFilePath))
                    {
                        e.Options.SubtitlesSource = srtFilePath;
                    }
                }*/

                // Handle Variant Bitrates
                if (this.isStream && e.Options.VideoStream.Metadata.ContainsKey("variant_bitrate"))
                {
                    Debug.WriteLine("Variant Bitrate detected, choosing highest");

                    var videoStreams = e.Info.Streams.Where(kvp => kvp.Value.CodecType == AVMediaType.AVMEDIA_TYPE_VIDEO).Select(kvp => kvp.Value);
                    foreach (var stream in videoStreams)
                    {
                        // Choose the best stream somehow
                        try
                        {
                            e.Options.VideoStream.Metadata.TryGetValue("variant_bitrate", out string currentBitRateString);
                            stream.Metadata.TryGetValue("variant_bitrate", out string bitRateString);

                            int currentBitRate = int.Parse(currentBitRateString, NumberFormatInfo.InvariantInfo);
                            int bitRate = int.Parse(bitRateString, NumberFormatInfo.InvariantInfo);

                            if (bitRate > currentBitRate)
                            {
                                e.Options.VideoStream = stream;
                            }
                        }
                        catch
                        {
                            /* Ignored */
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Fired when the video is loaded and ready to seek
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaOpened(object sender, Unosquare.FFME.Common.MediaOpenedEventArgs e)
        {
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaOpened: " + this.Id + " Opened, seek to: " + this._position), LogType.Audit.ToString());

            // Try to seek
            if (this._position > 0)
            {
                this.mediaElement.Position = TimeSpan.FromSeconds(this._position);
            }

            // Play
            this.mediaElement.Play();
        }

        /// <summary>
        /// Handles the MessageLogged event of the Media control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="MediaLogMessageEventArgs" /> instance containing the event data.</param>
        private void MediaElement_MediaMessageLogged(object sender, MediaLogMessageEventArgs e)
        {
            if (e.MessageType == MediaLogMessageType.Trace)
                return;

            Debug.WriteLine(e);
        }

        /// <summary>
        /// Stop
        /// </summary>
        public override void Stopped()
        {
            Trace.WriteLine(new LogMessage("Video", "Stopped: " + this.Id), LogType.Audit.ToString());

            // Remove the event handlers
            this.mediaElement.MediaOpening -= MediaElement_MediaOpening;
            this.mediaElement.MediaOpened -= MediaElement_MediaOpened;
            this.mediaElement.Loaded -= MediaElement_Loaded;
            this.mediaElement.MediaEnded -= MediaElement_MediaEnded;
            this.mediaElement.MediaFailed -= MediaElement_MediaFailed;
            this.mediaElement.MessageLogged -= MediaElement_MediaMessageLogged;

            // Try and clear some memory
            this.mediaElement.Close();
            this.mediaElement = null;

            base.Stopped();
        }
    }
}
