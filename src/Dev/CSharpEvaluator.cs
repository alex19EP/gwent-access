#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace GwentAccess.Dev
{
    /// <summary>
    /// Roslyn C# REPL for the dev server. RTAccess uses Mono.CSharp, which needs the Mono runtime; BepInEx IL2CPP
    /// runs plugins on CoreCLR, so this uses Roslyn scripting instead. State persists across calls (variables and
    /// usings from one eval are visible to the next).
    ///
    /// Compile runs on the HTTP thread (the first one takes seconds and would freeze the game); Run must happen on
    /// the game's main thread, since evaluated code touches Unity and IL2CPP objects.
    /// </summary>
    internal sealed class CSharpEvaluator
    {
        private ScriptOptions _options;
        private ScriptState<object> _state;

        private void Initialize()
        {
            // Reference every loaded assembly, plus every interop assembly the game hasn't loaded yet, once each by
            // name (two copies of one assembly make every type in it ambiguous).
            var refs = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) continue;
                if (!seen.Add(asm.GetName().Name)) continue;
                try { refs.Add(MetadataReference.CreateFromFile(asm.Location)); } catch { }
            }
            // The module is byte-loaded, so it has no file to reference: use its image. Only the CURRENT generation:
            // older ones are still loaded and carry the same types, which would make every module type ambiguous.
            if (ModuleLoader.CurrentImage != null)
                refs.Add(MetadataReference.CreateFromImage(ModuleLoader.CurrentImage));
            try
            {
                string interop = Path.Combine(BepInEx.Paths.BepInExRootPath, "interop");
                foreach (var file in Directory.GetFiles(interop, "*.dll"))
                {
                    if (!seen.Add(Path.GetFileNameWithoutExtension(file))) continue;
                    try { refs.Add(MetadataReference.CreateFromFile(file)); } catch { }
                }
            }
            catch (Exception e) { Plugin.LogInfo("Dev eval: interop folder scan failed: " + e.Message); }

            _options = ScriptOptions.Default
                .WithReferences(refs)
                .WithImports("System", "System.Linq", "System.Collections.Generic", "UnityEngine",
                    "Il2CppInterop.Runtime", "GwentAccess", "GwentAccess.Dev");
            Plugin.LogInfo("Dev eval: " + refs.Count + " references");
        }

        /// <summary>HTTP thread: compile against the current session. Null + errors text on failure.</summary>
        public Script<object> Compile(string code, out string errors)
        {
            if (_options == null) Initialize();
            Script<object> script = _state != null ? _state.Script.ContinueWith(code, _options) : CSharpScript.Create(code, _options);
            var diags = script.Compile().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors = diags.Count > 0 ? "[compile] " + string.Join("\n", diags) + "\n" : null;
            return diags.Count > 0 ? null : script;
        }

        /// <summary>Main thread: run a compiled script, capturing console output, the result and any exception.</summary>
        public string Run(Script<object> script)
        {
            var output = new StringWriter();
            TextWriter origOut = Console.Out, origErr = Console.Error;
            Console.SetOut(output);
            Console.SetError(output);
            ScriptState<object> next = null;
            Exception thrown = null;
            try
            {
                var task = _state != null ? script.RunFromAsync(_state, _ => true) : script.RunAsync(null, _ => true);
                next = task.GetAwaiter().GetResult();
                thrown = next.Exception;
            }
            catch (Exception e) { thrown = e; }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }
            if (next != null) _state = next;

            var sb = new StringBuilder();
            string captured = output.ToString();
            if (captured.Length > 0) { sb.Append(captured); if (!captured.EndsWith("\n")) sb.Append('\n'); }
            if (thrown != null) sb.Append("[exception] ").Append(thrown).Append('\n');
            else if (next?.ReturnValue != null) sb.Append("=> ").Append(next.ReturnValue).Append('\n');
            if (sb.Length == 0) sb.Append("(ok)\n");
            return sb.ToString();
        }

        /// <summary>Forget all variables and usings from earlier evals.</summary>
        public void Reset() { _state = null; }

        /// <summary>After a module reload: drop the session AND the reference set, so the next eval binds against
        /// the new module generation.</summary>
        public void Rebind() { _state = null; _options = null; }
    }
}
#endif
