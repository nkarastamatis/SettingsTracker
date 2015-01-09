﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace SettingsTracking.Serialization
{
    public class BinarySerializer : ISerializer
    {
        BinaryFormatter _formatter = new BinaryFormatter();

        public virtual byte[] Serialize(object obj)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                _formatter.Serialize(ms, obj);
                return ms.GetBuffer();
            }
        }

        public virtual object Deserialize(byte[] bytes)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return _formatter.Deserialize(ms);
            }
        }
    }
}
