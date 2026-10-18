// Copyright (c) the JPEG XL Project Authors. All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System;

namespace JxlSharp.Base
{
    /// <summary>
    /// Executes a callback upon disposal unless disarmed.
    /// </summary>
    public sealed class ScopeGuard : IDisposable
    {
        private readonly Action _callback;
        private bool _armed;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeGuard"/> class.
        /// </summary>
        /// <param name="callback">Callback to invoke on disposal.</param>
        /// <param name="armed">Whether the guard is initially armed.</param>
        public ScopeGuard(Action callback, bool armed = true)
        {
            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            _callback = callback;
            _armed    = armed;
        }

        /// <summary>
        /// Creates an armed <see cref="ScopeGuard"/> for the specified callback.
        /// </summary>
        /// <param name="callback">Callback to invoke on disposal.</param>
        /// <returns>A new armed <see cref="ScopeGuard"/>.</returns>
        public static ScopeGuard Create(Action callback)
        {
            return new ScopeGuard(callback, true);
        }

        /// <summary>
        /// Disarms the guard so the callback is not executed upon disposal.
        /// </summary>
        public void Disarm()
        {
            _armed = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_armed)
            {
                _armed = false;
                _callback();
            }
        }
    }
}
