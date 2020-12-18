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
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace XiboClient.Rendering
{
    class Video : VideoMedia
    {
        private bool _openCalled;

        /// <summary>
        /// The Media element for Playback
        /// </summary>
        private MediaElement mediaElement;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public Video(RegionOptions options) : base(options)
        {
            
        }

        /// <summary>
        /// Media Failed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            // Failed is the opposite of open, but we mark this as open called so that our watchman doesn't also try to expire
            this._openCalled = true;

            // Log and expire
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaFailed: " + this.Id + " Media Failed. E = " + e.ErrorException.Message), LogType.Error.ToString());

            // Add this to a temporary blacklist so that we don't repeat it too quickly
            CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, LayoutId, Id, "Video Failed: " + e.ErrorException.Message, 120);

            // Expire
            SignalElapsedEvent();
        }

        /// <summary>
        /// Media Ended
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaEnded: " + this.Id + " Ended, looping: " + isLooping), LogType.Audit.ToString());

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
            Trace.WriteLine(new LogMessage("Video", "MediaElement_Loaded: " + this.Id + " Control loaded, calling Play."), LogType.Audit.ToString());

            try
            {
                this.mediaElement.Play();
            }
            catch (Exception ex)
            {
                // Problem calling play, we should expire.
                Trace.WriteLine(new LogMessage("Video", "MediaElement_Loaded: " + this.Id + " Media Failed. E = " + ex.Message), LogType.Error.ToString());

                SignalElapsedEvent();
            }

            // We make a watchman to check that the video actually gets loaded.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (timerSender, args) =>
            {
                // You only tick once
                timer.Stop();

                // Check to see if open has been called.
                if (!_openCalled)
                {
                    Trace.WriteLine(new LogMessage("Video", "MediaElement_Loaded: " + this.Id + " Open not called after 4 seconds, marking unsafe and Expiring."), LogType.Error.ToString());

                    // Add this to a temporary blacklist so that we don't repeat it too quickly
                    CacheManager.Instance.AddUnsafeItem(UnsafeItemType.Media, LayoutId, Id, "Video Failed: Open not called after 4 seconds", 120);

                    // Expire
                    SignalElapsedEvent();
                }
            };

            timer.Start();
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

            // Create a Media Element
            this.mediaElement = new MediaElement();
            this.mediaElement.Volume = this.volume;
            this.mediaElement.IsMuted = this.Muted;
            this.mediaElement.LoadedBehavior = MediaState.Manual;
            this.mediaElement.UnloadedBehavior = MediaState.Close;

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
            // MediaOpened is called after we've called Play()
            this.mediaElement.MediaOpened += MediaElement_MediaOpened;

            // Loaded is from the Framework and is called when the MediaElement is added to the visual tree (we call play in here)
            this.mediaElement.Loaded += MediaElement_Loaded;

            // Media ended is called when the media file has finished playing
            this.mediaElement.MediaEnded += MediaElement_MediaEnded;

            // Media Failed is called if the media file cannot be opened
            this.mediaElement.MediaFailed += MediaElement_MediaFailed;

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
                this.mediaElement.Source = uri;

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
        /// Fired when the video is loaded and ready to seek
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine(new LogMessage("Video", "MediaElement_MediaOpened: " + this.Id + " Opened, seek to: " + this._position), LogType.Audit.ToString());

            // Open has been called.
            this._openCalled = true;

            // Try to seek
            if (this._position > 0)
            {
                this.mediaElement.Position = TimeSpan.FromSeconds(this._position);
            }
        }

        /// <summary>
        /// Stop
        /// </summary>
        public override void Stopped()
        {
            Trace.WriteLine(new LogMessage("Video", "Stopped: " + this.Id), LogType.Audit.ToString());

            // Remove the event handlers
            this.mediaElement.MediaOpened -= MediaElement_MediaOpened;
            this.mediaElement.Loaded -= MediaElement_Loaded;
            this.mediaElement.MediaEnded -= MediaElement_MediaEnded;
            this.mediaElement.MediaFailed -= MediaElement_MediaFailed;

            // Try and clear some memory
            this.mediaElement.Close();
            this.mediaElement.Clock = null;
            this.mediaElement.Source = null;
            this.mediaElement = null;

            base.Stopped();
        }
    }
}
