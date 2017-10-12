using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;

namespace VPI.Entities
{
    public static partial class Enums
    {
        public enum  OriginEnum
        {
            Existing,
            Trigger
        }
    }
}
