using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VPI.Entities
{
    public class DeleteMessage
    {
        public bool DeleteAll { get; set; } 

        public string TargetContainer { get; set; }
    }
}
