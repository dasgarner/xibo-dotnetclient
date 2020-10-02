﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using XiboClient.Logic;
using XiboClient.Stats;

namespace XiboClient.Rendering
{
    /// <summary>
    /// Interaction logic for Region.xaml
    /// </summary>
    public partial class Region : UserControl
    {
        /// <summary>
        /// Has the Layout Expired?
        /// </summary>
        public bool IsLayoutExpired = false;

        /// <summary>
        /// Has this Region Expired?
        /// </summary>
        public bool IsExpired = false;

        /// <summary>
        /// Is this region paused?
        /// </summary>
        private bool _isPaused = false;

        /// <summary>
        /// Is Pause Pending?
        /// </summary>
        private bool IsPausePending = false;

        /// <summary>
        /// This Regions zIndex
        /// </summary>
        public int ZIndex { get; set; }

        /// <summary>
        /// The Region Options
        /// </summary>
        private RegionOptions options;

        /// <summary>
        /// Current Media
        /// </summary>
        private Media currentMedia;

        /// <summary>
        /// Track the current sequence
        /// </summary>
        private int currentSequence = -1;
        private bool _sizeResetRequired;
        private bool _dimensionsSet = false;
        private int _audioSequence;
        private double _currentPlaytime;

        /// <summary>
        /// Event to indicate that this Region's duration has elapsed
        /// </summary>
        public delegate void DurationElapsedDelegate();
        public event DurationElapsedDelegate DurationElapsedEvent;

        /// <summary>
        /// Event to indicate that some media has expired.
        /// </summary>
        public delegate void MediaExpiredDelegate();
        public event MediaExpiredDelegate MediaExpiredEvent;

        public Region()
        {
            InitializeComponent();
            ZIndex = 0;
        }

        public void loadFromOptions(RegionOptions options)
        {
            // Start of by setting our dimensions
            SetDimensions(options.left, options.top, options.width, options.height);

            // Store the options
            this.options = options;
        }

        /// <summary>
        /// Start
        /// </summary>
        public void Start()
        {
            // Start this region
            this.currentSequence = -1;
            StartNext(0);
        }

        /// <summary>
        /// Start the Next Media
        /// <paramref name="position"/>
        /// </summary>
        private void StartNext(double position)
        {
            Debug.WriteLine("StartNext: Region " + this.options.regionId + " starting next sequence " + this.currentSequence + " at position " + position, "Region");

            if (!this._dimensionsSet)
            {
                // Evaluate the width, etc
                SetDimensions(this.options.left, this.options.top, this.options.width, this.options.height);

                // We've set the dimensions
                this._dimensionsSet = true;
            }

            // Try to populate a new media object for this region
            Media newMedia;

            // Loop around trying to start the next media
            bool startSuccessful = false;
            int countTries = 0;

            while (!startSuccessful)
            {
                // If we go round this the same number of times as media objects, then we are unsuccessful and should exception
                if (countTries >= this.options.mediaNodes.Count)
                    throw new ArgumentOutOfRangeException("Unable to set and start a media node");

                // Lets try again
                countTries++;

                // Only use the position if this is the first Widget we've tried to start.
                // otherwise start from the beginning
                if (countTries > 1)
                {
                    position = 0;
                }

                // Store the current sequence
                int temp = this.currentSequence;

                // Before we can try to set the next media node, we need to stop any currently running Audio
                StopAudio();

                // Set the next media node for this panel
                if (!SetNextMediaNodeInOptions())
                {
                    // For some reason we cannot set a media node... so we need this region to become invalid
                    throw new InvalidOperationException("Unable to set any region media nodes.");
                }

                // If the sequence hasnt been changed, OR the layout has been expired
                // there has been no change to the sequence, therefore the media we have already created is still valid
                // or this media has actually been destroyed and we are working out way out the call stack
                if (IsLayoutExpired)
                {
                    return;
                }
                else if (this.currentSequence == temp)
                {
                    // Media has not changed, we are likely the only valid media item in the region
                    // the layout has not yet expired, so depending on whether we loop or not, we either
                    // reload the same media item again
                    // or do nothing (return)
                    // This could be made more succinct, but is clearer written as an elseif.
                    if (!this.options.RegionLoop)
                    {
                        return;
                    }
                }

                // Store the Current Index
                this.options.CurrentIndex = this.currentSequence;

                // See if we can start the new media object
                try
                {
                    newMedia = CreateNextMediaNode();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("Region", "StartNext: Unable to create new " + this.options.type + "  object: " + ex.Message), LogType.Error.ToString());

                    // Try the next node
                    startSuccessful = false;
                    continue;
                }

                // New Media record has been created
                // ------------
                // Start the new media
                try
                {
                    // See if we need to change our Region Dimensions
                    if (newMedia.RegionSizeChangeRequired())
                    {
                        SetDimensions(0, 0, this.options.PlayerWidth, this.options.PlayerHeight);

                        // Set size reset for the next time around.
                        _sizeResetRequired = true;
                    }
                    else if (_sizeResetRequired)
                    {
                        SetDimensions(this.options.left, this.options.top, this.options.width, this.options.height);
                        _sizeResetRequired = false;
                    }

                    Debug.WriteLine("StartNext: Calling start on media in regionId " + this.options.regionId + ", position " + position, "Region");

                    // Start.
                    StartMedia(newMedia, position);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("Region", "StartNext: Unable to start new " + this.options.type + "  object: " + ex.Message), LogType.Error.ToString());
                    startSuccessful = false;
                    continue;
                }

                startSuccessful = true;

                // Remove the old media
                if (currentMedia != null)
                {
                    Debug.WriteLine("StartNext: Stopping current media in regionId " + this.options.regionId + ", position " + position, "Region");

                    StopMedia(currentMedia);

                    currentMedia = null;
                }

                // Change the reference 
                currentMedia = newMedia;

                // Open a stat record
                // options accurately reflect the current media, so we can use them.
                StatManager.Instance.WidgetStart(this.options.scheduleId, this.options.layoutId, this.options.mediaid);
            }
        }

        /// <summary>
        /// Sets the next media node. Should be used either from a mediaComplete event, or an options reset from 
        /// the parent.
        /// </summary>
        private bool SetNextMediaNodeInOptions()
        {
            // What if there are no media nodes?
            if (this.options.mediaNodes.Count == 0)
            {
                Trace.WriteLine(new LogMessage("Region", "SetNextMediaNode: No media nodes to display"), LogType.Audit.ToString());

                return false;
            }

            // Zero out the options that are persisted
            this.options.text = "";
            this.options.documentTemplate = "";
            this.options.copyrightNotice = "";
            this.options.scrollSpeed = 30;
            this.options.updateInterval = 6;
            this.options.uri = "";
            this.options.direction = "none";
            this.options.javaScript = "";
            this.options.FromDt = DateTime.MinValue;
            this.options.ToDt = DateTime.MaxValue;
            this.options.Dictionary = new MediaDictionary();

            // Tidy up old audio if necessary
            foreach (Media audio in this.options.Audio)
            {
                try
                {
                    // Unbind any events and dispose
                    audio.DurationElapsedEvent -= audio_DurationElapsedEvent;
                    audio.Stop(false);
                }
                catch
                {
                    Trace.WriteLine(new LogMessage("Region", "SetNextMediaNodeInOptions: Unable to dispose of audio item"), LogType.Audit.ToString());
                }
            }

            // Empty the options node
            this.options.Audio.Clear();

            // Get a media node
            bool validNode = false;
            int numAttempts = 0;

            // Loop through all the nodes in order
            while (numAttempts < this.options.mediaNodes.Count)
            {
                // Move the sequence on
                this.currentSequence++;

                if (this.currentSequence >= this.options.mediaNodes.Count)
                {
                    Trace.WriteLine(new LogMessage("Region", "SetNextMediaNodeInOptions: Region " + this.options.regionId + " Expired"), LogType.Audit.ToString());

                    // Start from the beginning
                    this.currentSequence = 0;

                    // We have expired (want to raise an expired event to the parent)
                    IsExpired = true;

                    // Region Expired
                    DurationElapsedEvent?.Invoke();

                    // We want to continue on to show the next media (unless the duration elapsed event triggers a region change)
                    if (IsLayoutExpired)
                    {
                        return true;
                    }
                }

                // Get the media node for this sequence
                XmlNode mediaNode = this.options.mediaNodes[this.currentSequence];
                XmlAttributeCollection nodeAttributes = mediaNode.Attributes;

                // Set the media id
                if (nodeAttributes["id"].Value != null)
                    this.options.mediaid = nodeAttributes["id"].Value;

                // Set the file id
                if (nodeAttributes["fileId"] != null)
                {
                    this.options.FileId = int.Parse(nodeAttributes["fileId"].Value);
                }

                // Check isnt blacklisted
                if (BlackList.Instance.BlackListed(this.options.mediaid))
                {
                    Trace.WriteLine(new LogMessage("Region - SetNextMediaNode", string.Format("MediaID [{0}] has been blacklisted.", this.options.mediaid)), LogType.Error.ToString());

                    // Increment the number of attempts and try again
                    numAttempts++;

                    // Carry on
                    continue;
                }

                // Stats enabled?
                this.options.isStatEnabled = (nodeAttributes["enableStat"] == null) ? true : (int.Parse(nodeAttributes["enableStat"].Value) == 1);

                // Parse the options for this media node
                ParseOptionsForMediaNode(mediaNode, nodeAttributes);

                // Is this widget inside the from/to date?
                if (!(this.options.FromDt <= DateTime.Now && this.options.ToDt > DateTime.Now))
                {
                    Trace.WriteLine(new LogMessage("Region", "SetNextMediaNode: Widget outside from/to date."), LogType.Audit.ToString());

                    // Increment the number of attempts and try again
                    numAttempts++;

                    // Carry on
                    continue;
                }

                // Assume we have a valid node at this point
                validNode = true;

                // Is this a file based media node?
                if (this.options.type == "video" || this.options.type == "flash" || this.options.type == "image" || this.options.type == "powerpoint" || this.options.type == "audio" || this.options.type == "htmlpackage")
                {
                    // Use the cache manager to determine if the file is valid
                    validNode = CacheManager.Instance.IsValidPath(this.options.uri);
                }

                // If we have a valid node, break out of the loop
                if (validNode)
                    break;

                // Increment the number of attempts and try again
                numAttempts++;
            }

            // If we dont have a valid node out of all the nodes in the region, then return false.
            if (!validNode)
                return false;

            Trace.WriteLine(new LogMessage("Region - SetNextMediaNode", "New media detected " + this.options.type), LogType.Audit.ToString());

            return true;
        }

        /// <summary>
        /// Parse options for the media node
        /// </summary>
        /// <param name="mediaNode"></param>
        /// <param name="nodeAttributes"></param>
        private void ParseOptionsForMediaNode(XmlNode mediaNode, XmlAttributeCollection nodeAttributes)
        {
            // New version has a different schema - the right way to do it would be to pass the <options> and <raw> nodes to 
            // the relevant media class - however I dont feel like engineering such a change so the alternative is to
            // parse all the possible media type nodes here.

            // Type and Duration will always be on the media node
            this.options.type = nodeAttributes["type"].Value;

            // Render as
            if (nodeAttributes["render"] != null)
                this.options.render = nodeAttributes["render"].Value;

            //TODO: Check the type of node we have, and make sure it is supported.

            if (nodeAttributes["duration"].Value != "")
            {
                this.options.duration = int.Parse(nodeAttributes["duration"].Value);
            }
            else
            {
                this.options.duration = 60;
                Trace.WriteLine("Duration is Empty, using a default of 60.", "Region - SetNextMediaNode");
            }

            // Widget From/To dates (v2 onward)
            try
            {
                if (nodeAttributes["fromDt"] != null)
                {
                    this.options.FromDt = DateTime.Parse(nodeAttributes["fromDt"].Value, CultureInfo.InvariantCulture);
                }

                if (nodeAttributes["toDt"] != null)
                {
                    this.options.ToDt = DateTime.Parse(nodeAttributes["toDt"].Value, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                Trace.WriteLine(new LogMessage("Region", "ParseOptionsForMediaNode: Unable to parse widget from/to dates."), LogType.Error.ToString());
            }

            // We cannot have a 0 duration here... not sure why we would... but
            if (this.options.duration == 0 && this.options.type != "video" && this.options.type != "localvideo")
            {
                int emptyLayoutDuration = int.Parse(ApplicationSettings.Default.EmptyLayoutDuration.ToString());
                this.options.duration = (emptyLayoutDuration == 0) ? 10 : emptyLayoutDuration;
            }

            // There will be some stuff on option nodes
            XmlNode optionNode = mediaNode.SelectSingleNode("options");

            // Track if an update interval has been provided in the XLF
            bool updateIntervalProvided = false;

            // Loop through each option node
            foreach (XmlNode option in optionNode.ChildNodes)
            {
                if (option.Name == "direction")
                {
                    this.options.direction = option.InnerText;
                }
                else if (option.Name == "uri")
                {
                    this.options.uri = option.InnerText;
                }
                else if (option.Name == "copyright")
                {
                    this.options.copyrightNotice = option.InnerText;
                }
                else if (option.Name == "scrollSpeed")
                {
                    try
                    {
                        this.options.scrollSpeed = int.Parse(option.InnerText);
                    }
                    catch
                    {
                        System.Diagnostics.Trace.WriteLine("Non integer scrollSpeed in XLF", "Region - SetNextMediaNode");
                    }
                }
                else if (option.Name == "updateInterval")
                {
                    updateIntervalProvided = true;

                    try
                    {
                        this.options.updateInterval = int.Parse(option.InnerText);
                    }
                    catch
                    {
                        // Update interval not defined, so assume a high value
                        this.options.updateInterval = 3600;

                        Trace.WriteLine("Non integer updateInterval in XLF", "Region - SetNextMediaNode");
                    }
                }

                // Add this to the options object
                this.options.Dictionary.Add(option.Name, option.InnerText);
            }

            // And some stuff on Raw nodes
            XmlNode rawNode = mediaNode.SelectSingleNode("raw");

            if (rawNode != null)
            {
                foreach (XmlNode raw in rawNode.ChildNodes)
                {
                    if (raw.Name == "text")
                    {
                        this.options.text = raw.InnerText;
                    }
                    else if (raw.Name == "template")
                    {
                        this.options.documentTemplate = raw.InnerText;
                    }
                    else if (raw.Name == "embedHtml")
                    {
                        this.options.text = raw.InnerText;
                    }
                    else if (raw.Name == "embedScript")
                    {
                        this.options.javaScript = raw.InnerText;
                    }
                }
            }

            // Audio Nodes?
            XmlNode audio = mediaNode.SelectSingleNode("audio");

            if (audio != null)
            {
                foreach (XmlNode audioNode in audio.ChildNodes)
                {
                    RegionOptions options = new RegionOptions();
                    options.Dictionary = new MediaDictionary();
                    options.duration = 0;
                    options.uri = ApplicationSettings.Default.LibraryPath + @"\" + audioNode.InnerText;

                    if (audioNode.Attributes["loop"] != null)
                    {
                        options.Dictionary.Add("loop", audioNode.Attributes["loop"].Value);

                        if (options.Dictionary.Get("loop", 0) == 1)
                        {
                            // Set the media duration to be equal to the duration of the parent media
                            options.duration = (this.options.duration == 0) ? int.MaxValue : this.options.duration;
                        }
                    }

                    if (audioNode.Attributes["volume"] != null)
                        options.Dictionary.Add("volume", audioNode.Attributes["volume"].Value);

                    Media audioMedia = new Audio(options);

                    // Bind to the media complete event
                    audioMedia.DurationElapsedEvent += audio_DurationElapsedEvent;

                    this.options.Audio.Add(audioMedia);
                }
            }

            // Media Types without an update interval should have a sensible default (xibosignage/xibo#404)
            // This means that items which do not provide an update interval will still refresh.
            if (!updateIntervalProvided)
            {
                // Special handling for text/webpages because we know they should never have a default update interval applied
                if (this.options.type == "webpage" || this.options.type == "text")
                {
                    // Very high (will expire eventually, but shouldn't cause a routine request for a new resource
                    this.options.updateInterval = int.MaxValue;
                }
                else
                {
                    // Default to 5 minutes for those items that do not provide an update interval
                    this.options.updateInterval = 5;
                }
            }
        }

        /// <summary>
        /// Create the next media node based on the provided options
        /// </summary>
        /// <returns></returns>
        private Media CreateNextMediaNode()
        {
            Media media;

            // Grab a local copy of options
            RegionOptions options = this.options;

            Trace.WriteLine(new LogMessage("Region - CreateNextMediaNode", string.Format("Creating new media: {0}, {1}", options.type, options.mediaid)), LogType.Audit.ToString());

            // We've set our next media node in options already
            // this includes checking that file based media is valid.
            switch (options.type)
            {
                case "image":
                    options.uri = ApplicationSettings.Default.LibraryPath + @"\" + options.uri;
                    media = new Image(options);
                    break;

                case "powerpoint":
                    options.uri = ApplicationSettings.Default.LibraryPath + @"\" + options.uri;
                    media = new PowerPoint(options);
                    break;

                case "video":
                    options.uri = ApplicationSettings.Default.LibraryPath + @"\" + options.uri;
                    media = VideoMedia.GetConfiguredVideoMedia(options);
                    break;

                case "localvideo":
                    // Local video does not update the URI with the library path, it just takes what has been provided in the Widget.
                    media = VideoMedia.GetConfiguredVideoMedia(options);
                    break;

                case "audio":
                    options.uri = ApplicationSettings.Default.LibraryPath + @"\" + options.uri;
                    media = new Audio(options);
                    break;

                case "embedded":
                    media = WebMedia.GetConfiguredWebMedia(options, WebMedia.ReadBrowserType(this.options.text));
                    break;

                case "datasetview":
                case "ticker":
                case "text":
                case "webpage":
                    media = WebMedia.GetConfiguredWebMedia(options);
                    break;

                case "flash":
                    options.uri = ApplicationSettings.Default.LibraryPath + @"\" + options.uri;
                    media = new Flash(options);
                    break;

                case "shellcommand":
                    media = new ShellCommand(options);
                    break;

                case "htmlpackage":
                    media = WebMedia.GetConfiguredWebMedia(options);
                    ((WebMedia)media).ConfigureForHtmlPackage();
                    break;

                case "spacer":
                    media = new Spacer(options);
                    break;

                case "hls":
                    if (ApplicationSettings.Default.FfmpegAvailable && ApplicationSettings.Default.UseFFmpegForHls)
                    {
                        media = new VideoFFmpeg(options);
                    }
                    else
                    {
                        media = new WebEdge(options);
                    }
                    break;

                default:
                    if (options.render == "html")
                    {
                        media = WebMedia.GetConfiguredWebMedia(options);
                    }
                    else
                    {
                        throw new InvalidOperationException("Not a valid media node type: " + options.type);
                    }
                    break;
            }

            // Set the media width/height
            media.Width = Width;
            media.Height = Height;

            // Sets up the timer for this media, if it hasn't already been set
            if (media.Duration == 0)
            {
                media.Duration = options.duration;
            }

            // Add event handler for when this completes
            media.DurationElapsedEvent += new Media.DurationElapsedDelegate(media_DurationElapsedEvent);

            return media;
        }

        /// <summary>
        /// Start the provided media
        /// </summary>
        /// <param name="media"></param>
        private void StartMedia(Media media, double position)
        {
            Trace.WriteLine(new LogMessage("Region", "StartMedia: Starting media at position: " + position), LogType.Audit.ToString());

            // Add to this scene
            this.RegionScene.Children.Add(media);

            // Render the media, this adds the child controls to the Media UserControls grid
            media.RenderMedia(position);

            // Reset the audio sequence and start
            _audioSequence = 1;
            startAudio();
        }

        /// <summary>
        /// Start Audio if necessary
        /// </summary>
        private void startAudio()
        {
            // Start any associated audio
            if (this.options.Audio.Count >= _audioSequence)
            {
                Media audio = this.options.Audio[_audioSequence - 1];

                // call render media and add to controls
                audio.RenderMedia(0);

                // Add to this scene
                this.RegionScene.Children.Add(audio);
            }
        }

        /// <summary>
        /// Audio Finished Playing
        /// </summary>
        /// <param name="filesPlayed"></param>
        private void audio_DurationElapsedEvent(int filesPlayed)
        {
            try
            {
                StopMedia(this.options.Audio[_audioSequence - 1]);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("Region - audio_DurationElapsedEvent", "Audio -  Unable to dispose. Ex = " + ex.Message), LogType.Audit.ToString());
            }

            _audioSequence += filesPlayed;

            // Start
            startAudio();
        }

        /// <summary>
        /// Stop Media
        /// </summary>
        /// <param name="media"></param>
        private void StopMedia(Media media)
        {
            StopMedia(media, false);
        }

        /// <summary>
        /// Stop normal media node
        /// </summary>
        /// <param name="media"></param>
        /// <param name="regionStopped"></param>
        private void StopMedia(Media media, bool regionStopped)
        {
            Trace.WriteLine(new LogMessage("Region", "StopMedia: Stopping..."), LogType.Audit.ToString());

            // Dispose of the current media
            try
            {
                // Close the stat record
                StatManager.Instance.WidgetStop(media.ScheduleId, media.LayoutId, media.Id, media.StatsEnabled);

                // Media Stopped Event removes the media from the scene
                media.MediaStoppedEvent += Media_MediaStoppedEvent;

                // Tidy Up
                media.DurationElapsedEvent -= media_DurationElapsedEvent;
                media.Stop(regionStopped);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(new LogMessage("Region", "StopMedia: Unable to stop. Ex = " + ex.Message), LogType.Audit.ToString());

                // Remove the controls
                RegionScene.Children.Remove(media);
            }
        }

        /// <summary>
        /// This media has stopped
        /// </summary>
        /// <param name="media"></param>
        private void Media_MediaStoppedEvent(Media media)
        {
            media.MediaStoppedEvent -= Media_MediaStoppedEvent;
            media.Stopped();

            // Remove the controls
            RegionScene.Children.Remove(media);
        }

        /// <summary>
        /// Stop Audio
        /// </summary>
        private void StopAudio()
        {
            // Stop the currently playing audio (if there is any)
            if (this.options.Audio.Count > 0)
            {
                try
                {
                    StopMedia(this.options.Audio[_audioSequence - 1]);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(new LogMessage("Region - Stop Media", "Audio -  Unable to dispose. Ex = " + ex.Message), LogType.Audit.ToString());
                }
            }
        }

        /// <summary>
        /// The media has elapsed
        /// </summary>
        private void media_DurationElapsedEvent(int filesPlayed)
        {
            Trace.WriteLine(new LogMessage("Region", string.Format("DurationElapsedEvent: Media Elapsed: {0}", this.options.uri)), LogType.Audit.ToString());

            if (filesPlayed > 1)
            {
                // Increment the _current sequence by the number of filesPlayed (minus 1)
                this.currentSequence = this.currentSequence + (filesPlayed - 1);
            }

            // Indicate that this media has expired.
            MediaExpiredEvent?.Invoke();

            // If this layout has been expired we know that everything will soon be torn down, so do nothing
            if (IsLayoutExpired)
            {
                Debug.WriteLine("DurationElapsedEvent: Layout Expired, therefore we don't StartNext", "Region");
                return;
            }

            // If we are now paused, we don't start the next media
            if (this._isPaused)
            {
                Debug.WriteLine("DurationElapsedEvent: Paused, therefore we don't StartNext", "Region");
                return;
            }

            // If Pause Pending, then stop here as we will be removed
            if (IsPausePending)
            {
                Debug.WriteLine("DurationElapsedEvent: Pause Pending, therefore we don't StartNext", "Region");
                return;
            }

            // TODO:
            // Animate out at this point if we need to
            // the result of the animate out complete event should then move us on.
            // this.currentMedia.TransitionOut();

            // make some decisions about what to do next
            try
            {
                StartNext(0);
            }
            catch (Exception e)
            {
                Trace.WriteLine(new LogMessage("Region", "DurationElapsedEvent: E=" + e.Message), LogType.Error.ToString());

                // What do we do if there is an exception moving to the next media node?
                // For some reason we cannot set a media node... so we need this region to become invalid
                IsExpired = true;

                // Fire elapsed
                DurationElapsedEvent?.Invoke();

                return;
            }
        }

        /// <summary>
        /// Set Dimensions
        /// </summary>
        /// <param name="left"></param>
        /// <param name="top"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        private void SetDimensions(int left, int top, int width, int height)
        {
            Debug.WriteLine("Setting Dimensions to W:" + width + ", H:" + height + ", (" + left + "," + top + ")");

            // Evaluate the width, etc
            Width = width;
            Height = height;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(left, top, 0, 0);
        }

        /// <summary>
        /// Set Dimensions
        /// </summary>
        /// <param name="location"></param>
        /// <param name="size"></param>
        private void SetDimensions(Point location, Size size)
        {
            SetDimensions((int)location.X, (int)location.Y, (int)size.Width, (int)size.Height);
        }

        /// <summary>
        /// Is Pause Pending?
        /// </summary>
        public void PausePending()
        {
            this.IsPausePending = true;
        }

        /// <summary>
        /// Pause this Layout
        /// </summary>
        public void Pause()
        {
            if (this.currentMedia != null)
            {
                // Store the current playtime of this Region.
                this._currentPlaytime = this.currentMedia.CurrentPlaytime();

                // Stop and remove the current media.
                StopMedia(this.currentMedia, true);

                // Remove it.
                this.currentMedia = null;

                Debug.WriteLine("Pause: paused Region, current Playtime is " + this._currentPlaytime, "Region");
            }

            // Paused
            this._isPaused = true;
            this.IsPausePending = false;
        }

        /// <summary>
        /// Resume this Layout
        /// </summary>
        public void Resume(bool isInterrupt)
        {
            // If we are an interrupt, we should skip on to the next item
            // and if there is only 1 item, we should replay it.
            // if we are a normal layout, then we resume the current one.
            if (isInterrupt)
            {
                if (this.options.mediaNodes.Count <= 1)
                {
                    this.currentSequence--;
                }

                // Resume the current media item
                StartNext(0);
            }
            else
            {
                // We have to dial back the current position here, because start next will straight away increment it
                this.currentSequence--;

                // Resume the current media item
                StartNext(this._currentPlaytime);
            }

            this._isPaused = false;
        }

        /// <summary>
        /// Clears the Region of anything that it shouldnt still have... 
        /// called when Destroying a Layout and when Removing an Overlay
        /// </summary>
        public void Clear()
        {
            try
            {
                // Stop Audio
                StopAudio();

                // Stop the current media item
                if (this.currentMedia != null)
                {
                    StopMedia(this.currentMedia);
                }
            }
            catch
            {
                Trace.WriteLine(new LogMessage("Region - Clear", "Error closing off stat record"), LogType.Error.ToString());
            }
        }
    }
}
