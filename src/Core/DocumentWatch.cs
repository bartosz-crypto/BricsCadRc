using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Podpina handlery zdarzeń do KAŻDEGO otwartego rysunku — także otwieranych później.
    ///
    /// Wcześniej watchery podpinały się tylko do MdiActiveDocument w chwili NETLOAD,
    /// więc w drugim i kolejnych rysunkach nie działały (COPY remap, stretch prętów,
    /// flush etykiet, snap MLeaderów, podświetlenie).
    ///
    /// Użycie:
    ///   DocumentWatch.Subscribe("BarCopyWatcher", doc => {...attach...}, doc => {...detach...});
    ///   DocumentWatch.Unsubscribe("BarCopyWatcher");
    /// </summary>
    public static class DocumentWatch
    {
        private sealed class Subscription
        {
            public Action<Document> Attach;
            public Action<Document> Detach;
            public readonly HashSet<Document> Attached = new HashSet<Document>();
        }

        private static readonly Dictionary<string, Subscription> _subs =
            new Dictionary<string, Subscription>(StringComparer.Ordinal);
        private static bool _hooked;

        public static void Subscribe(string key, Action<Document> attach, Action<Document> detach)
        {
            if (_subs.ContainsKey(key)) return;
            EnsureHooked();

            var sub = new Subscription { Attach = attach, Detach = detach };
            _subs[key] = sub;

            foreach (Document doc in Application.DocumentManager)
                AttachOne(key, sub, doc);
        }

        public static void Unsubscribe(string key)
        {
            if (!_subs.TryGetValue(key, out var sub)) return;
            foreach (var doc in new List<Document>(sub.Attached))
                DetachOne(key, sub, doc);
            _subs.Remove(key);

            if (_subs.Count == 0) Unhook();
        }

        /// <summary>Komendy, po których NIE wolno nic poprawiać w rysunku (zepsułoby REDO).</summary>
        public static bool IsUndoCommand(string globalCommandName)
        {
            string c = (globalCommandName ?? "").ToUpperInvariant();
            return c == "U" || c == "UNDO" || c == "REDO" || c == "MREDO" || c == "OOPS";
        }

        // ----------------------------------------------------------------

        private static void EnsureHooked()
        {
            if (_hooked) return;
            Application.DocumentManager.DocumentCreated       += OnDocumentCreated;
            Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
            _hooked = true;
        }

        private static void Unhook()
        {
            if (!_hooked) return;
            try { Application.DocumentManager.DocumentCreated       -= OnDocumentCreated;       } catch (System.Exception logEx) { Log.Error("DocumentWatch.Unhook", logEx); }
            try { Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed; } catch (System.Exception logEx) { Log.Error("DocumentWatch.Unhook", logEx); }
            _hooked = false;
        }

        private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document == null) return;
            foreach (var kv in _subs)
                AttachOne(kv.Key, kv.Value, e.Document);
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
        {
            if (e.Document == null) return;
            foreach (var kv in _subs)
                DetachOne(kv.Key, kv.Value, e.Document);
        }

        private static void AttachOne(string key, Subscription sub, Document doc)
        {
            if (doc == null || sub.Attached.Contains(doc)) return;
            try
            {
                sub.Attach(doc);
                sub.Attached.Add(doc);
            }
            catch (System.Exception ex) { Log.Error($"DocumentWatch.Attach[{key}]", ex); }
        }

        private static void DetachOne(string key, Subscription sub, Document doc)
        {
            if (doc == null || !sub.Attached.Remove(doc)) return;
            try { sub.Detach(doc); }
            catch (System.Exception ex) { Log.Error($"DocumentWatch.Detach[{key}]", ex); }
        }
    }
}
