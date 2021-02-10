﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.GameObjects.EntitySystems;
using Content.Client.Utility;
using Content.Shared.Construction;
using Content.Shared.GameObjects.Components;
using Content.Shared.GameObjects.Components.Interactable;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.Interfaces.Placement;
using Robust.Client.Interfaces.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Construction
{
    [GenerateTypedNameReferences]
    public partial class ConstructionMenu : SS14Window
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IEntitySystemManager _systemManager = default!;
        [Dependency] private readonly IPlacementManager _placementManager = default!;

        protected override Vector2? CustomSize => (720, 320);

        private ConstructionPrototype? _selected;
        private string[] _categories = Array.Empty<string>();

        public ConstructionMenu()
        {
            IoCManager.InjectDependencies(this);
            RobustXamlLoader.Load(this);

            _placementManager.PlacementChanged += PlacementChanged;

            Title = Loc.GetString("Construction");

            BuildButton.Text = Loc.GetString("Place construction ghost");
            RecipesList.OnItemSelected += RecipeSelected;
            RecipesList.OnItemDeselected += RecipeDeselected;

            SearchBar.OnTextChanged += SearchTextChanged;
            Category.OnItemSelected += CategorySelected;

            BuildButton.Text = Loc.GetString("Place construction ghost");
            BuildButton.OnToggled += BuildButtonToggled;
            ClearButton.Text = Loc.GetString("Clear All");
            ClearButton.OnPressed += ClearAllButtonPressed;
            EraseButton.Text = Loc.GetString("Eraser Mode");
            EraseButton.OnToggled += EraseButtonToggled;

            PopulateCategories();
            PopulateAll();
        }

        private void PlacementChanged(object? sender, EventArgs e)
        {
            BuildButton.Pressed = false;
            EraseButton.Pressed = false;
        }

        private void PopulateAll()
        {
            foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                RecipesList.Add(GetItem(recipe, RecipesList));
            }
        }

        private static ItemList.Item GetItem(ConstructionPrototype recipe, ItemList itemList)
        {
            return new(itemList)
            {
                Metadata = recipe,
                Text = recipe.Name,
                Icon = recipe.Icon.Frame0(),
                TooltipEnabled = true,
                TooltipText = recipe.Description,
            };
        }

        private void PopulateBy(string search, string category)
        {
            RecipesList.Clear();

            foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                if (!string.IsNullOrEmpty(search))
                {
                    if (!recipe.Name.ToLowerInvariant().Contains(search.Trim().ToLowerInvariant()))
                        continue;
                }

                if (!string.IsNullOrEmpty(category) && category != Loc.GetString("All"))
                {
                    if (recipe.Category != category)
                        continue;
                }

                RecipesList.Add(GetItem(recipe, RecipesList));
            }
        }

        private void PopulateCategories()
        {
            var uniqueCategories = new HashSet<string>();

            // hard-coded to show all recipes
            uniqueCategories.Add(Loc.GetString("All"));

            foreach (var prototype in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
            {
                var category = Loc.GetString(prototype.Category);

                if (!string.IsNullOrEmpty(category))
                    uniqueCategories.Add(category);
            }

            Category.Clear();

            var array = uniqueCategories.ToArray();
            Array.Sort(array);

            for (var i = 0; i < array.Length; i++)
            {
                var category = array[i];
                Category.AddItem(category, i);
            }

            _categories = array;
        }

        private void PopulateInfo(ConstructionPrototype prototype)
        {
            ClearInfo();

            var isItem = prototype.Type == ConstructionType.Item;

            BuildButton.Disabled = false;
            BuildButton.Text = Loc.GetString(!isItem ? "Place construction ghost" : "Craft");
            TargetName.SetMessage(prototype.Name);
            TargetDesc.SetMessage(prototype.Description);
            TargetTexture.Texture = prototype.Icon.Frame0();

            if (!_prototypeManager.TryIndex(prototype.Graph, out ConstructionGraphPrototype graph))
                return;

            var startNode = graph.Nodes[prototype.StartNode];
            var targetNode = graph.Nodes[prototype.TargetNode];

            var path = graph.Path(startNode.Name, targetNode.Name);

            var current = startNode;

            var stepNumber = 1;

            Texture? GetTextureForStep(ConstructionGraphStep step)
            {
                switch (step)
                {
                    case MaterialConstructionGraphStep materialStep:
                        switch (materialStep.Material)
                        {
                            case StackType.Metal:
                                return _resourceCache.GetTexture("/Textures/Objects/Materials/sheets.rsi/metal.png");

                            case StackType.Glass:
                                return _resourceCache.GetTexture("/Textures/Objects/Materials/sheets.rsi/glass.png");

                            case StackType.Plasteel:
                                return _resourceCache.GetTexture("/Textures/Objects/Materials/sheets.rsi/plasteel.png");

                            case StackType.Plasma:
                                return _resourceCache.GetTexture("/Textures/Objects/Materials/sheets.rsi/plasma.png");

                            case StackType.Cable:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/cables.rsi/coil-30.png");

                            case StackType.MetalRod:
                                return _resourceCache.GetTexture("/Textures/Objects/Materials/materials.rsi/rods.png");

                        }
                        break;

                    case ToolConstructionGraphStep toolStep:
                        switch (toolStep.Tool)
                        {
                            case ToolQuality.Anchoring:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/wrench.rsi/icon.png");
                            case ToolQuality.Prying:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/crowbar.rsi/icon.png");
                            case ToolQuality.Screwing:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/screwdriver.rsi/screwdriver-map.png");
                            case ToolQuality.Cutting:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/wirecutters.rsi/cutters-map.png");
                            case ToolQuality.Welding:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/welder.rsi/welder.png");
                            case ToolQuality.Multitool:
                                return _resourceCache.GetTexture("/Textures/Objects/Tools/multitool.rsi/multitool.png");
                        }

                        break;

                    case ComponentConstructionGraphStep componentStep:
                        return componentStep.Icon?.Frame0();

                    case PrototypeConstructionGraphStep prototypeStep:
                        return prototypeStep.Icon?.Frame0();

                    case NestedConstructionGraphStep _:
                        return null;
                }

                return null;
            }

            foreach (var node in path)
            {
                var edge = current.GetEdge(node.Name);
                var firstNode = current == startNode;

                if (firstNode)
                {
                    StepList.AddItem(isItem
                        ? Loc.GetString($"{stepNumber++}. To craft this item, you need:")
                        : Loc.GetString($"{stepNumber++}. To build this, first you need:"));
                }

                foreach (var step in edge.Steps)
                {
                    var icon = GetTextureForStep(step);

                    switch (step)
                    {
                        case MaterialConstructionGraphStep materialStep:
                            StepList.AddItem(
                                !firstNode
                                    ? Loc.GetString(
                                        "{0}. Add {1}x {2}.", stepNumber++, materialStep.Amount, materialStep.Material)
                                    : Loc.GetString("      {0}x {1}", materialStep.Amount, materialStep.Material), icon);

                            break;

                        case ToolConstructionGraphStep toolStep:
                            StepList.AddItem(Loc.GetString("{0}. Use a {1}.", stepNumber++, toolStep.Tool.GetToolName()), icon);
                            break;

                        case PrototypeConstructionGraphStep prototypeStep:
                            StepList.AddItem(Loc.GetString("{0}. Add {1}.", stepNumber++, prototypeStep.Name), icon);
                            break;

                        case ComponentConstructionGraphStep componentStep:
                            StepList.AddItem(Loc.GetString("{0}. Add {1}.", stepNumber++, componentStep.Name), icon);
                            break;

                        case NestedConstructionGraphStep nestedStep:
                            var parallelNumber = 1;
                            StepList.AddItem(Loc.GetString("{0}. In parallel...", stepNumber++));

                            foreach (var steps in nestedStep.Steps)
                            {
                                var subStepNumber = 1;

                                foreach (var subStep in steps)
                                {
                                    icon = GetTextureForStep(subStep);

                                    switch (subStep)
                                    {
                                        case MaterialConstructionGraphStep materialStep:
                                            if (!isItem)
                                                StepList.AddItem(Loc.GetString("    {0}.{1}.{2}. Add {3}x {4}.", stepNumber, parallelNumber, subStepNumber++, materialStep.Amount, materialStep.Material), icon);
                                            break;

                                        case ToolConstructionGraphStep toolStep:
                                            StepList.AddItem(Loc.GetString("    {0}.{1}.{2}. Use a {3}.", stepNumber, parallelNumber, subStepNumber++, toolStep.Tool.GetToolName()), icon);
                                            break;

                                        case PrototypeConstructionGraphStep prototypeStep:
                                            StepList.AddItem(Loc.GetString("    {0}.{1}.{2}. Add {3}.", stepNumber, parallelNumber, subStepNumber++, prototypeStep.Name), icon);
                                            break;

                                        case ComponentConstructionGraphStep componentStep:
                                            StepList.AddItem(Loc.GetString("    {0}.{1}.{2}. Add {3}.", stepNumber, parallelNumber, subStepNumber++, componentStep.Name), icon);
                                            break;
                                    }
                                }

                                parallelNumber++;
                            }

                            break;
                    }
                }

                current = node;
            }
        }

        private void ClearInfo()
        {
            BuildButton.Disabled = true;
            TargetName.SetMessage(string.Empty);
            TargetDesc.SetMessage(string.Empty);
            TargetTexture.Texture = null;
            StepList.Clear();
        }

        private void RecipeSelected(ItemList.ItemListSelectedEventArgs obj)
        {
            _selected = (ConstructionPrototype) obj.ItemList[obj.ItemIndex].Metadata!;
            PopulateInfo(_selected);
        }

        private void RecipeDeselected(ItemList.ItemListDeselectedEventArgs obj)
        {
            _selected = null;
            ClearInfo();
        }

        private void CategorySelected(OptionButton.ItemSelectedEventArgs obj)
        {
            Category.SelectId(obj.Id);
            PopulateBy(SearchBar.Text, _categories[obj.Id]);
        }

        private void SearchTextChanged(LineEdit.LineEditEventArgs obj)
        {
            PopulateBy(SearchBar.Text, _categories[Category.SelectedId]);
        }

        private void BuildButtonToggled(BaseButton.ButtonToggledEventArgs args)
        {
            if (args.Pressed)
            {
                if (_selected == null) return;

                var constructSystem = EntitySystem.Get<ConstructionSystem>();

                if (_selected.Type == ConstructionType.Item)
                {
                    constructSystem.TryStartItemConstruction(_selected.ID);
                    BuildButton.Pressed = false;
                    return;
                }

                _placementManager.BeginPlacing(new PlacementInformation()
                {
                    IsTile = false,
                    PlacementOption = _selected.PlacementMode,
                }, new ConstructionPlacementHijack(constructSystem, _selected));
            }
            else
            {
                _placementManager.Clear();
            }

            BuildButton.Pressed = args.Pressed;
        }

        private void EraseButtonToggled(BaseButton.ButtonToggledEventArgs args)
        {
            if (args.Pressed) _placementManager.Clear();
            _placementManager.ToggleEraserHijacked(new ConstructionPlacementHijack(_systemManager.GetEntitySystem<ConstructionSystem>(), null));
            EraseButton.Pressed = args.Pressed;
        }

        private void ClearAllButtonPressed(BaseButton.ButtonEventArgs obj)
        {
            var constructionSystem = EntitySystem.Get<ConstructionSystem>();

            constructionSystem.ClearAllGhosts();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _placementManager.PlacementChanged -= PlacementChanged;
            }
        }
    }
}
