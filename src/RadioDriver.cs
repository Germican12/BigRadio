using System;
using UnityEngine;

namespace BigRadio
{
    /// <summary>Persistent helper object: polls clip loading and the reload hotkey every frame.</summary>
    public class RadioDriver : MonoBehaviour
    {
        public RadioDriver(IntPtr ptr) : base(ptr) { }

        private void Update() => Core.Tick();
    }
}
