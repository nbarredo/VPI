using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OrchestrationFunctions.Helpers
{
    public static class Extensions
    {
        public static string GetFileNameWithoutExtension(this string s)
        {
          return s.Remove(s.IndexOf('.'));
        }
    }
}
