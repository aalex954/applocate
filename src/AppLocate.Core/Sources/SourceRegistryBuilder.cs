using AppLocate.Core.Abstractions;

namespace AppLocate.Core.Sources {
    /// <summary>
    /// Fluent builder for assembling an <see cref="ISourceRegistry"/> with ordering and replacement semantics.
    /// Supports extension points (plugins) without modifying the CLI composition root.
    /// </summary>
    public sealed class SourceRegistryBuilder {
        private readonly List<ISource> _sources = [];
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
        private bool _built;

        /// <summary>Adds a source instance. Fails if a source with the same <see cref="ISource.Name"/> already exists.</summary>
        /// <exception cref="InvalidOperationException">Thrown if build already finalized or duplicate name.</exception>
        public SourceRegistryBuilder Add(ISource source) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            EnsureNotBuilt();
            if (!_names.Add(source.Name)) {
                throw new InvalidOperationException($"Source '{source.Name}' already registered.");
            }

            _sources.Add(source);
            return this;
        }

        /// <summary>Replaces an existing source with the same name or adds if missing.</summary>
        public SourceRegistryBuilder AddOrReplace(ISource source) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            EnsureNotBuilt();
            for (var i = 0; i < _sources.Count; i++) {
                if (string.Equals(_sources[i].Name, source.Name, StringComparison.OrdinalIgnoreCase)) {
                    _sources[i] = source; // preserve position
                    return this;
                }
            }
            _sources.Add(source);
            _ = _names.Add(source.Name);
            return this;
        }

        /// <summary>Removes a source by name if present (no error if not found).</summary>
        public SourceRegistryBuilder Remove(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return this;
            }

            EnsureNotBuilt();
            for (var i = 0; i < _sources.Count; i++) {
                if (string.Equals(_sources[i].Name, name, StringComparison.OrdinalIgnoreCase)) {
                    _sources.RemoveAt(i);
                    _ = _names.Remove(name);
                    break;
                }
            }
            return this;
        }

        /// <summary>Inserts a source before another named source; if target not found behaves like <see cref="Add"/>.</summary>
        public SourceRegistryBuilder InsertBefore(string existingName, ISource source) {
            if (source == null) {
                throw new ArgumentNullException(nameof(source));
            }

            EnsureNotBuilt();
            if (!_names.Add(source.Name)) {
                throw new InvalidOperationException($"Source '{source.Name}' already registered.");
            }

            for (var i = 0; i < _sources.Count; i++) {
                if (string.Equals(_sources[i].Name, existingName, StringComparison.OrdinalIgnoreCase)) {
                    _sources.Insert(i, source);
                    return this;
                }
            }
            _sources.Add(source); // fallback append
            return this;
        }

        /// <summary>Moves a registered source to a new index (clamped); no-op if name not present.</summary>
        public SourceRegistryBuilder Move(string name, int newIndex) {
            EnsureNotBuilt();
            var idx = _sources.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) {
                return this;
            }

            var item = _sources[idx];
            _sources.RemoveAt(idx);
            if (newIndex < 0) {
                newIndex = 0;
            }

            if (newIndex > _sources.Count) {
                newIndex = _sources.Count;
            }

            _sources.Insert(newIndex, item);
            return this;
        }

        /// <summary>Finalizes and returns an immutable <see cref="ISourceRegistry"/>.</summary>
        public ISourceRegistry Build() {
            EnsureNotBuilt();
            _built = true;
            // materialize defensive copy
            return new SourceRegistry(_sources.ToArray());
        }

        private void EnsureNotBuilt() {
            if (_built) {
                throw new InvalidOperationException("Builder already used to construct a registry.");
            }
        }
    }
}
