using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Mathematics;

namespace Spine.Utils
{
    internal class SpineUtils
    {
        /// <summary>
        /// Given a value in range [from1, to1], remap it to the corresponding value in range [from2, to2]. Example: Remap(5, 0, 10, 0, 100) returns 50.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="from1"></param>
        /// <param name="to1"></param>
        /// <param name="from2"></param>
        /// <param name="to2"></param>
        /// <returns></returns>
        public static float Remap(
            float value,
            float from1,
            float to1,
            float from2,
            float to2)
        {
            return math.remap(from1, to1, from2, to2, value);
        }
    }
}
