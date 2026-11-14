using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace Alpaca4d.UIWidgets
{
    public class EvaluationUnit
    {
        private string name;
        private string displayName;
        private string description;
        private List<ExtendedPlug> inputs;
        private List<ExtendedPlug> outputs;
        private bool active;
        private Bitmap icon;
        private bool keepLinks;
        private EvaluationUnitContext cxt;
        private GH_SwitcherComponent compontent;
        public GH_SwitcherComponent Component
        {
            get
            {
                return compontent;
            }
            set
            {
                compontent = value;
            }
        }
        public List<ExtendedPlug> Inputs => inputs;
        public List<ExtendedPlug> Outputs => outputs;
        public string Name
        {
            get
            {
                return name;
            }
            set
            {
                name = value;
            }
        }
        public string DisplayName
        {
            get
            {
                return displayName;
            }
            set
            {
                displayName = value;
            }
        }
        public string Description
        {
            get
            {
                return description;
            }
            set
            {
                description = value;
            }
        }
        public bool Active
        {
            get
            {
                return active;
            }
            set
            {
                active = value;
            }
        }
        public bool KeepLinks
        {
            get
            {
                return keepLinks;
            }
            set
            {
                keepLinks = value;
            }
        }
        public Bitmap Icon
        {
            get
            {
                return icon;
            }
            set
            {
                icon = value;
            }
        }
        public EvaluationUnitContext Context => cxt;

        public EvaluationUnit(string name, string displayName, string description, Bitmap icon = null)
        {
            this.name = name;
            this.displayName = displayName;
            this.description = description;
            inputs = new List<ExtendedPlug>();
            outputs = new List<ExtendedPlug>();
            keepLinks = false;
            this.icon = icon;
            cxt = new EvaluationUnitContext(this);
        }
        private static Type GetGenericType(Type generic, Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                Type right = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == right)
                {
                    return toCheck;
                }
                toCheck = toCheck.BaseType;
            }
            return null;
        }
        public void RegisterInputParam(IGH_Param param, string name, string nickName, string description, GH_ParamAccess access, IGH_Goo defaultValue)
        {            
            param.Name = name;
            param.NickName = nickName;
            param.Description = description;
            param.Access = access;
            try
            {
                //if (defaultValue != null && typeof(IGH_Goo).IsAssignableFrom(param.GetType()))
                if (defaultValue != null)
                {
                    Type genericType = GetGenericType(typeof(GH_PersistentParam<>), ((object)param).GetType());
                    if (genericType != null)
                    {
                        Type[] genericArguments = genericType.GetGenericArguments();
                        if (genericArguments.Length != 0)
                        {
                            Type type = genericArguments[0];
                            genericType.GetMethod("SetPersistentData", BindingFlags.Instance | BindingFlags.Public, null, new Type[1]
                            {
                                genericArguments[0]
                            }, null).Invoke(param, new object[1]
                            {
                            defaultValue
                            });
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            ExtendedPlug extendedPlug = new ExtendedPlug(param);
            extendedPlug.Unit = this;
            inputs.Add(extendedPlug);
        }

        public void RegisterInputParam(IGH_Param param, string name, string nickName, string description, GH_ParamAccess access)
        {
              RegisterInputParam(param, name, nickName, description, access, null);
        }

        public void RegisterOutputParam(IGH_Param param, string name, string nickName, string description)
        {
            param.Name = name;
            param.NickName = nickName;
            param.Description = description;
            ExtendedPlug extendedPlug = new ExtendedPlug(param);
            outputs.Add(extendedPlug);
        }

        public void NewParameterIds()
        {
            foreach (ExtendedPlug input in inputs)
            {
                input.Parameter.NewInstanceGuid();
            }
            foreach (ExtendedPlug output in outputs)
            {
                output.Parameter.NewInstanceGuid();
            }
        }

        public void AddMenu(GH_ExtendableMenu menu)
        {
            Context.Collection.AddMenu(menu);
        }

        public bool Write(GH_IWriter writer)
        {
            writer.SetString("name", Name);
            GH_IWriter val = writer.CreateChunk("params");
            GH_IWriter val2 = val.CreateChunk("input");
            val2.SetInt32("index", 0);
            val2.SetInt32("count", Inputs.Count);
            for (int i = 0; i < inputs.Count; i++)
            {
                if (inputs[i].Parameter.Attributes != null)
                {
                    GH_IWriter val3 = val2.CreateChunk("p", i);
                    inputs[i].Parameter.Write(val3);
                }
            }
            GH_IWriter val4 = val.CreateChunk("output");
            val4.SetInt32("index", 0);
            val4.SetInt32("count", Outputs.Count);
            for (int j = 0; j < outputs.Count; j++)
            {
                if (outputs[j].Parameter.Attributes != null)
                {
                    GH_IWriter val5 = val4.CreateChunk("p", j);
                    outputs[j].Parameter.Write(val5);
                }
            }
            GH_IWriter writer2 = writer.CreateChunk("context");
            return cxt.Collection.Write(writer2);
        }

        public bool Read(GH_IReader reader)
        {
            if (reader.ChunkExists("params"))
            {
                GH_IReader val = reader.FindChunk("params");
                if (val.ChunkExists("input") && inputs != null)
                {
                    ReadParams(val.FindChunk("input"), inputs);
                }
                if (val.ChunkExists("output") && outputs != null)
                {
                    ReadParams(val.FindChunk("output"), outputs);
                }
            }
            try
            {
                GH_IReader val4 = reader.FindChunk("context");
                if (val4 != null)
                {
                    cxt.Collection.Read(val4);
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine("Component error:" + ex.Message + "\n" + ex.StackTrace);
            }
            return true;
        }

        /// <summary>
        /// Reads each saved parameter into the one of its name, or failing that into the one in its
        /// place - an input renamed since, as ν was once v.
        ///
        /// This read by position alone, and only when the file had exactly as many parameters as the
        /// unit has now - otherwise none at all. So a unit given an input since a file was saved, as
        /// the materials were given Colour, came back from that file with every value and every wire
        /// gone. New inputs go last, so a file with no more parameters than the unit has now still
        /// lines up with its first ones; one with more is read by name alone.
        /// </summary>
        private static void ReadParams(GH_IReader reader, List<ExtendedPlug> plugs)
        {
            int count = -1;
            if (!reader.TryGetInt32("count", ref count)) return;

            var chunks = new List<GH_IReader>();
            var names = new List<string>();
            for (int i = 0; i < count; i++)
            {
                GH_IReader chunk = reader.ChunkExists("p", i) ? reader.FindChunk("p", i)
                                 : reader.ChunkExists("param", i) ? reader.FindChunk("param", i)
                                 : null;
                string name = null;
                chunk?.TryGetString("Name", ref name);
                chunks.Add(chunk);
                names.Add(name);
            }

            var read = new HashSet<ExtendedPlug>();
            for (int i = 0; i < count; i++)
            {
                if (chunks[i] == null) continue;

                ExtendedPlug plug = plugs.FirstOrDefault(p => p.Parameter.Name == names[i] && !read.Contains(p));
                if (plug == null && count <= plugs.Count && !read.Contains(plugs[i]) && !names.Contains(plugs[i].Parameter.Name))
                    plug = plugs[i];
                if (plug == null) continue;

                read.Add(plug);
                plug.Parameter.Read(chunks[i]);
            }
        }

        public string GetMenuDescription()
        {
            return Context.Collection.GetMenuDescription();
        }


    }
}
