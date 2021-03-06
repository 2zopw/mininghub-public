﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MiningHub.Core.Extensions
{
    public static class SerializationExtensions
    {
        public static T SafeExtensionDataAs<T>(this IDictionary<string, object> extra)
        {
            if (extra != null)
            {
                try
                {
                    return JToken.FromObject(extra).ToObject<T>();
                }

                catch(Exception)
                {
                    // ignored
                }
            }

            return default(T);
        }
    }
}
