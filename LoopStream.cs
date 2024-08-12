﻿using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoleplayingMediaCore {
    public class LoopStream : WaveStream {
        WaveStream sourceStream;
        private bool _LoopEarly;

        /// <summary>
        /// Creates a new Loop stream
        /// </summary>
        /// <param name="sourceStream">The stream to read from. Note: the Read method of this stream should return 0 when it reaches the end
        /// or else we will not loop to the start again.</param>
        public LoopStream(WaveStream sourceStream) {
            this.sourceStream = sourceStream;
            this.EnableLooping = true;
        }

        /// <summary>
        /// Use this to turn looping on or off
        /// </summary>
        public bool EnableLooping { get; set; }
        MediaObject _parent;

        /// <summary>
        /// Return source stream's wave format
        /// </summary>
        public override WaveFormat WaveFormat {
            get { return sourceStream.WaveFormat; }
        }

        /// <summary>
        /// LoopStream simply returns
        /// </summary>
        public override long Length {
            get { return sourceStream.Length; }
        }

        /// <summary>
        /// LoopStream simply passes on positioning to source stream
        /// </summary>
        public override long Position {
            get { return sourceStream.Position; }
            set { sourceStream.Position = value; }
        }

        public MediaObject Parent { get => _parent; set => _parent = value; }

        public override int Read(byte[] buffer, int offset, int count) {
            int totalBytesRead = 0;

            while (totalBytesRead < count) {
                int bytesRead = sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                if (bytesRead == 0 || _LoopEarly) {
                    if (sourceStream.Position == 0 || (!EnableLooping && !Parent.Invalidated)) {
                        // something wrong with the source stream
                        break;
                    }
                    // loop
                    sourceStream.Position = 0;
                    _LoopEarly = false;
                }
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }

        internal void LoopEarly() {
            _LoopEarly = true;
        }
    }
}
