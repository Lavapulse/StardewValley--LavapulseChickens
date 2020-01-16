using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LavapulseChickens
{
    class ModData
    {
        public Dictionary<string, string> packsLoaded = new Dictionary<string, string>();
        public Dictionary<string, List<int>> packsUnloaded = new Dictionary<string, List<int>>();
        public Dictionary<int, List<string>> customEggsAndHatchedAnimals = new Dictionary<int, List<string>>();
    }
}
