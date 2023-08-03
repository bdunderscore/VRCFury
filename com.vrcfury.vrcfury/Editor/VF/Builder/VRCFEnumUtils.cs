using System;
using System.Collections.Generic;

namespace VF.Builder {
    public class VRCFEnumUtils {
        public static string GetName(Enum enumval) {
            try {
                return Enum.GetName(enumval.GetType(), enumval);
            } catch(Exception) {
                return "Unknown Enum";
            }
        }

        public static T Parse<T>(string name) where T : Enum {
            try {
                return (T)Enum.Parse(typeof(T), name);
            } catch(Exception) {
                return default;
            }
        }
        
        public static T[] GetValues<T>() where T : Enum {
            var output = new List<T>();
            foreach (var i in Enum.GetValues(typeof(T))) {
                output.Add((T)i);
            }
            return output.ToArray();
        }
    }
}
