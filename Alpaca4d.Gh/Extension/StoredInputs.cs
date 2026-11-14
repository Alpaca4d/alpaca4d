using System;
using System.Collections.Generic;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Lines a component's inputs up with the ones a file saved, before Grasshopper reads them in.
    ///
    /// Grasshopper reads inputs by position, and only as many as the component has when the file
    /// opens. An input added to a component since a file was saved finds nothing in its place
    /// ("Input parameter chunk is missing. Archive is corrupt.") or the next input's data; and one a
    /// component adds for itself as it runs, as the Section Library adds Gap for double angles, is
    /// skipped, wires and all. Called from Read, this takes the names the file saved its inputs
    /// under and makes the component's inputs those, in that order: the ones it has, by name, any
    /// other from <c>make</c>, and one renamed since by its place. Inputs the file does not have are
    /// taken off for the component to put back once the file is read - see <see cref="Ensure"/>.
    /// </summary>
    internal static class StoredInputs
    {
        /// <summary>
        /// Arranges <paramref name="component"/>'s inputs as <paramref name="reader"/> saved them.
        /// <paramref name="make"/> builds an input the component makes for itself, by name, or
        /// returns null; one it cannot build leaves the inputs untouched, read by position as before.
        /// </summary>
        public static void Match(GH_Component component, GH_IReader reader, Func<string, IGH_Param> make)
        {
            var names = new List<string>();
            for (int i = 0; reader.ChunkExists("param_input", i); i++)
            {
                string name = null;
                if (!reader.FindChunk("param_input", i).TryGetString("Name", ref name)) return;
                names.Add(name);
            }
            if (names.Count == 0) return;

            var inputs = component.Params.Input;
            var wanted = new List<IGH_Param>();
            for (int i = 0; i < names.Count; i++)
            {
                var param = inputs.FirstOrDefault(p => p.Name == names[i] && !wanted.Contains(p))
                         ?? make(names[i])
                         ?? Renamed(inputs, i, names, wanted, make);
                if (param == null) return;
                wanted.Add(param);
            }

            if (inputs.SequenceEqual(wanted)) return;

            foreach (var param in inputs.ToList())
                component.Params.UnregisterInputParameter(param, false);
            foreach (var param in wanted)
                component.Params.RegisterInputParam(param);
            component.Params.OnParametersChanged();
        }

        /// <summary>
        /// The input in the place of saved input <paramref name="index"/>, when that one has a name
        /// the component no longer has: the same input, renamed since. Not one the file has under
        /// its own name, nor one the component makes for itself, such as Colour.
        /// </summary>
        private static IGH_Param Renamed(List<IGH_Param> inputs, int index, List<string> names, List<IGH_Param> wanted, Func<string, IGH_Param> make)
        {
            if (index >= inputs.Count) return null;
            var param = inputs[index];
            if (wanted.Contains(param) || names.Contains(param.Name) || make(param.Name) != null) return null;
            return param;
        }

        /// <summary>
        /// Puts <paramref name="param"/> at the end of the inputs if the component has none of its
        /// name: after a file saved without it has been read.
        /// </summary>
        public static void Ensure(GH_Component component, Func<IGH_Param> param, string name)
        {
            if (component.Params.Input.Any(p => p.Name == name)) return;

            component.Params.RegisterInputParam(param());
            component.Params.OnParametersChanged();
        }
    }
}
