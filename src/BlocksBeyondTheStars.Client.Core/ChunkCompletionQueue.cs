// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Main-thread completion queue: favour nearby terrain, but serve the oldest item
    /// every eighth selection so continuous nearby edits cannot starve distant completions.</summary>
    public sealed class ChunkCompletionQueue<T>
    {
        private readonly List<T> _items = new List<T>();
        private int _nearSelections;
        public int Count => _items.Count;
        public void Enqueue(T item) => _items.Add(item);

        public bool TryDequeue(Func<T, float> distanceSquared, out T item)
        {
            if (_items.Count == 0)
            {
                item = default!;
                return false;
            }

            int index = 0;
            if (_nearSelections < 7)
            {
                float nearest = distanceSquared(_items[0]);
                for (int i = 1; i < _items.Count; i++)
                {
                    float distance = distanceSquared(_items[i]);
                    if (distance < nearest) { nearest = distance; index = i; }
                }
                _nearSelections++;
            }
            else _nearSelections = 0;
            item = _items[index];
            _items.RemoveAt(index);
            return true;
        }
    }
}
