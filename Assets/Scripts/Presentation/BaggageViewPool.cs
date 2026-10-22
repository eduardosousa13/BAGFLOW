using System.Collections.Generic;
using UnityEngine;

namespace AirportBaggageSimulator
{
    /// <summary>Reuses BaggageSpriteView instances so that large N does not instantiate/destroy per frame.</summary>
    public sealed class BaggageViewPool
    {
        private readonly BaggageSpriteView prefab;
        private readonly Transform parent;
        private readonly Stack<BaggageSpriteView> free = new Stack<BaggageSpriteView>();

        public int TotalCreated { get; private set; }
        public int ActiveCount { get; private set; }
        public int FreeCount { get { return free.Count; } }

        public BaggageViewPool(BaggageSpriteView prefab, Transform parent)
        {
            this.prefab = prefab;
            this.parent = parent;
        }

        public BaggageSpriteView Acquire()
        {
            BaggageSpriteView view = free.Count > 0 ? free.Pop() : Create();
            view.gameObject.SetActive(true);
            ActiveCount++;
            return view;
        }

        public void Release(BaggageSpriteView view)
        {
            if (view == null) return;
            view.gameObject.SetActive(false);
            free.Push(view);
            ActiveCount--;
        }

        private BaggageSpriteView Create()
        {
            TotalCreated++;
            var view = Object.Instantiate(prefab, parent, false);
            view.name = "Bag";
            return view;
        }
    }
}
