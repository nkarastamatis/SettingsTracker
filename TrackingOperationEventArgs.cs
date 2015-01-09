﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace SettingsTracking
{
    public class TrackingOperationEventArgs : CancelEventArgs
    {
        public TrackingConfiguration Configuration { get; private set; }
        public TrackingOperationEventArgs(TrackingConfiguration configuration)
        {
            Configuration = configuration;
        }
    }
}
