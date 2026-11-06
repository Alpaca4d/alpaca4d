using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

using Alpaca4d.Explorer;

namespace Alpaca4d.Gh
{
    /// <summary>
    /// Something Explorer Inputs steps through: a number slider, a value list or a panel.
    ///
    /// Each has a number of steps and sits on one of them. Moving it to another never starts a
    /// solution by itself: the flight moves every input first and then solves once, so a design
    /// is never solved half-set. A slider and a value list are moved where they are, so the user
    /// sees the design on the canvas; a panel stays as it is, and Explorer Inputs hands on the line
    /// it is at through the output that goes with it.
    /// </summary>
    internal abstract class ExplorerSource
    {
        protected ExplorerSource(IGH_Param param)
        {
            Param = param;
        }

        public IGH_Param Param { get; }

        /// <summary>What it is, for messages: "slider", "value list", "panel".</summary>
        public abstract string Kind { get; }

        public abstract int StepCount { get; }
        public abstract int CurrentStep { get; }

        /// <summary>Moves to a step without solving anything.</summary>
        public abstract void Apply(int step);

        /// <summary>The value as it is written to data.csv.</summary>
        public abstract string Value { get; }

        /// <summary>The value as the rest of the definition gets it.</summary>
        public abstract IGH_Goo Goo { get; }

        /// <summary>
        /// The name of its column in the explorer: the nickname, or a slider's implied one. Null when
        /// it has only the name Grasshopper gave it, which would make "List" a column.
        /// </summary>
        public virtual string Name
        {
            get
            {
                string name = Param.NickName?.Trim();
                if (string.IsNullOrEmpty(name)) return null;
                if (name == "List" || name == "Panel" || name == "Input") return null;
                return name;
            }
        }

        /// <summary>
        /// The source for what is wired in, or null when it is not one Explorer Inputs can step
        /// through. A panel starts at <paramref name="panelStep"/>, the line it was last left at.
        /// </summary>
        public static ExplorerSource From(IGH_Param param, int panelStep)
        {
            if (param is GH_NumberSlider slider) return new SliderSource(slider);
            if (param is GH_ValueList list) return new ValueListSource(list);
            if (param is GH_Panel panel) return new PanelSource(panel, panelStep);
            return null;
        }
    }

    /// <summary>
    /// A slider's steps are its ticks: every value its accuracy allows between its ends. An even or
    /// odd slider's ticks include the values it rounds away, so only every other one is a step.
    /// The ticks are counted rather than listed, as a slider with five decimals has a great many.
    /// </summary>
    internal sealed class SliderSource : ExplorerSource
    {
        private readonly GH_NumberSlider _slider;
        private readonly int _first;
        private readonly int _stride;
        private readonly int _count;

        public SliderSource(GH_NumberSlider slider) : base(slider)
        {
            _slider = slider;
            int ticks = slider.TickCount;
            var accuracy = slider.Slider.Type;
            if (accuracy == GH_SliderAccuracy.Even || accuracy == GH_SliderAccuracy.Odd)
            {
                long minimum = (long)Math.Round(slider.Slider.Minimum);
                bool wantEven = accuracy == GH_SliderAccuracy.Even;
                _first = (Math.Abs(minimum) % 2 == 0) == wantEven ? 0 : 1;
                _stride = 2;
                _count = _first > ticks ? 0 : (ticks - _first) / 2 + 1;
            }
            else
            {
                _first = 0;
                _stride = 1;
                _count = ticks + 1;
            }
        }

        public override string Kind => "slider";
        public override int StepCount => _count;

        public override int CurrentStep
        {
            get
            {
                int step = (int)Math.Round((_slider.TickValue - _first) / (double)_stride);
                return Math.Min(Math.Max(step, 0), Math.Max(_count - 1, 0));
            }
        }

        public override void Apply(int step)
        {
            // A slider with no room to move has one step and is already on it; its tick space is
            // 0 / 0, which TickValue would throw on.
            if (_slider.TickCount <= 0) return;
            // TickValue sets the slider without raising its events and expires it without solving.
            _slider.TickValue = _first + step * _stride;
        }

        public override string Value => ExplorerValue.Format(_slider.CurrentValue);
        public override IGH_Goo Goo => new GH_Number(Convert.ToDouble(_slider.CurrentValue));

        // Not the implied nickname a slider shows when it has none: that is the name of the input
        // nearest it on the canvas, so moving components about would rename the column.
        public override string Name
        {
            get
            {
                string name = _slider.NickName?.Trim();
                return string.IsNullOrEmpty(name) ? null : name;
            }
        }
    }

    /// <summary>
    /// A value list's steps are its items, and the value written is the item's name: "HEA200"
    /// reads better on an axis than the expression behind it. The rest of the definition still gets
    /// the item's value, from the list itself.
    /// </summary>
    internal sealed class ValueListSource : ExplorerSource
    {
        private readonly GH_ValueList _list;

        public ValueListSource(GH_ValueList list) : base(list)
        {
            _list = list;
        }

        public override string Kind => "value list";
        public override int StepCount => _list.ListItems.Count;

        public override int CurrentStep
        {
            get
            {
                int index = _list.ListItems.FindIndex(item => item.Selected);
                return Math.Max(index, 0);
            }
        }

        public override void Apply(int step)
        {
            // Not SelectItem: that starts a solution of its own.
            var items = _list.ListItems;
            bool changed = false;
            for (int i = 0; i < items.Count; i++)
            {
                bool select = i == step;
                if (items[i].Selected != select)
                {
                    items[i].Selected = select;
                    changed = true;
                }
            }
            if (changed) _list.ExpireSolution(false);
        }

        public override string Value => _list.FirstSelectedItem?.Name ?? "";
        public override IGH_Goo Goo => _list.FirstSelectedItem?.Value;
    }

    /// <summary>A panel's steps are its lines, or whatever items it holds.</summary>
    internal sealed class PanelSource : ExplorerSource
    {
        private readonly List<IGH_Goo> _items;
        private int _step;

        public PanelSource(GH_Panel panel, int step) : base(panel)
        {
            _items = panel.VolatileData.AllData(true).ToList();
            _step = Math.Min(Math.Max(step, 0), Math.Max(_items.Count - 1, 0));
        }

        public override string Kind => "panel";
        public override int StepCount => _items.Count;
        public override int CurrentStep => _step;

        public override void Apply(int step)
        {
            _step = step;
        }

        public override string Value => _items.Count == 0 ? "" : _items[_step]?.ToString() ?? "";
        public override IGH_Goo Goo => _items.Count == 0 ? null : _items[_step];
    }
}
