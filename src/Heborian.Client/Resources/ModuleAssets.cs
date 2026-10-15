using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Heborian.Client.Resources
{
    /// <summary>Module-local assets. Unity texture calls must run on the Unity main thread.</summary>
    public sealed class ModuleAssets : IDisposable
    {
        private readonly string root;
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private bool disposed;

        public ModuleAssets(string root) => this.root = Path.GetFullPath(root).TrimEnd(new[] { Path.DirectorySeparatorChar }) + Path.DirectorySeparatorChar;

        public string Resolve(string relativePath)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ModuleAssets));
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw new ArgumentException("Use a relative module asset path.", nameof(relativePath));
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Asset path must stay inside its module directory.", nameof(relativePath));
            return path;
        }

        public byte[] ReadBytes(string relativePath) => File.ReadAllBytes(Resolve(relativePath));

        public Texture2D LoadTexture(string relativePath)
        {
            var bytes = ReadBytes(relativePath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, false))
                    throw new InvalidDataException("Asset is not a supported PNG/JPEG image: " + relativePath);
                texture.name = relativePath;
                textures.Add(texture);
                return texture;
            }
            catch { UnityEngine.Object.Destroy(texture); throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var texture in textures) UnityEngine.Object.Destroy(texture);
            textures.Clear();
        }
    }
}
