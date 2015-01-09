using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SettingsTracking.Serialization
{
    public class ExpandoSerializer : BinarySerializer
    {
        public override byte[] Serialize(object obj)
        {
            var dictionary = new Dictionary<string, object>(obj as IDictionary<string, object>);
            return base.Serialize(dictionary);
        }

        public override object Deserialize(byte[] bytes)
        {
            try
            {
                var obj = base.Deserialize(bytes);
                dynamic expando = new System.Dynamic.ExpandoObject();
                var expandoDictionary = expando as IDictionary<string, object>;
                foreach(var kvp in obj as IDictionary<string, object>)
                {
                    expandoDictionary[kvp.Key] = kvp.Value;
                }
                return expando;

            }
            catch (Exception ex)
            {
                var s = ex.ToString();
            }
            return null;
        }
    }
}
