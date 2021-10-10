﻿using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client.Interactable.Components;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;
using Vector2 = Robust.Shared.Maths.Vector2;

namespace Content.Client.ContextMenu.UI
{
    public abstract class ContextMenuElement : Control
    {
        private static readonly Color HoverColor = Color.DarkSlateGray;
        protected internal readonly ContextMenuPopup? ParentMenu;

        protected ContextMenuElement(ContextMenuPopup? parentMenu)
        {
            ParentMenu = parentMenu;
            MouseFilter = MouseFilterMode.Stop;
        }
        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            if (UserInterfaceManager.CurrentlyHovered == this)
            {
                handle.DrawRect(PixelSizeBox, HoverColor);
            }
        }
    }

    public sealed class SingleContextElement : ContextMenuElement
    {
        public event Action? OnMouseHovering;
        public event Action? OnExitedTree;

        public IEntity ContextEntity{ get; }
        public readonly StackContextElement? Pre;

        public ISpriteComponent? SpriteComp { get; }
        public InteractionOutlineComponent? OutlineComponent { get; }
        public int OriginalDrawDepth { get; }
        public bool DrawOutline { get; set; }

        public SingleContextElement(IEntity entity, StackContextElement? pre, ContextMenuPopup? parentMenu) : base(parentMenu)
        {
            Pre = pre;
            ContextEntity = entity;
            if (ContextEntity.TryGetComponent(out ISpriteComponent? sprite))
            {
                SpriteComp = sprite;
                OriginalDrawDepth = SpriteComp.DrawDepth;
            }
            OutlineComponent = ContextEntity.GetComponentOrNull<InteractionOutlineComponent>();

            AddChild(
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Horizontal,
                    Children =
                    {
                        new LayoutContainer
                        {
                            Children = { new SpriteView { Sprite = SpriteComp } }
                        },
                        new Label
                        {
                            Text = Loc.GetString(UserInterfaceManager.DebugMonitors.Visible ? $"{ContextEntity.Name} ({ContextEntity.Uid})" : ContextEntity.Name)
                        }
                    }, Margin = new Thickness(0,0,10,0)
                }
            );
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (UserInterfaceManager.CurrentlyHovered == this)
            {
                OnMouseHovering?.Invoke();
            }
        }

        protected override void ExitedTree()
        {
            OnExitedTree?.Invoke();
            base.ExitedTree();
        }
    }

    public sealed class StackContextElement : ContextMenuElement
     {
         public event Action? OnExitedTree;

         public HashSet<IEntity> ContextEntities { get; }
         public readonly StackContextElement? Pre;

         private readonly SpriteView _spriteView;
         private readonly Label _label;

         public int EntitiesCount => ContextEntities.Count;

         public StackContextElement(IEnumerable<IEntity> entities, StackContextElement? pre, ContextMenuPopup? parentMenu)
             : base(parentMenu)
         {
             Pre = pre;
             ContextEntities = new(entities);
             _spriteView = new SpriteView
             {
                 Sprite = ContextEntities.First().GetComponent<ISpriteComponent>()
             };
             _label = new Label
             {
                 Text = Loc.GetString(ContextEntities.Count.ToString()),
                 StyleClasses = { StyleNano.StyleClassContextMenuCount }
             };

             LayoutContainer.SetAnchorPreset(_label, LayoutContainer.LayoutPreset.BottomRight);
             LayoutContainer.SetGrowHorizontal(_label, LayoutContainer.GrowDirection.Begin);
             LayoutContainer.SetGrowVertical(_label, LayoutContainer.GrowDirection.Begin);

             AddChild(
                 new BoxContainer
                 {
                     Orientation = LayoutOrientation.Horizontal,
                     SeparationOverride = 6,
                     Children =
                     {
                         new LayoutContainer { Children = { _spriteView, _label } },
                         new BoxContainer
                         {
                             Orientation = LayoutOrientation.Horizontal,
                             SeparationOverride = 6,
                             Children =
                             {
                                 new Label
                                 {
                                     Text = Loc.GetString(ContextEntities.First().Name)
                                 },
                                 new TextureRect
                                 {
                                     Texture = IoCManager.Resolve<IResourceCache>().GetTexture("/Textures/Interface/VerbIcons/group.svg.192dpi.png"),
                                     TextureScale = (0.5f, 0.5f),
                                     Stretch = TextureRect.StretchMode.KeepCentered,
                                 }
                             }
                         }
                     }, Margin = new Thickness(0,0,10,0)
                 }
             );
         }

         protected override void ExitedTree()
         {
             OnExitedTree?.Invoke();
             base.ExitedTree();
         }

         public void RemoveEntity(IEntity entity)
         {
             ContextEntities.Remove(entity);

             _label.Text = Loc.GetString(ContextEntities.Count.ToString());
             _spriteView.Sprite = ContextEntities.FirstOrDefault(e => !e.Deleted)?.GetComponent<ISpriteComponent>();
         }
     }

    public class ContextMenuPopup : Popup
    {
        public static readonly Color ButtonColor = Color.FromHex("#1119");
        public static readonly Color BackgroundColor = Color.FromHex("#333E");

        public const int MaxItemsBeforeScroll = 10;
        public const int MarginSize = 2;
        public const int ButtonHeight = 32;

        public BoxContainer List { get; }
        public ScrollContainer Scroll { get; }
        public int Depth { get; }

        public ContextMenuPopup(int depth = 0)
        {
            MaxHeight = MaxItemsBeforeScroll * (ButtonHeight + 2*MarginSize);

            Depth = depth;
            List = new() { Orientation = LayoutOrientation.Vertical };
            Scroll = new()
            {
                HScrollEnabled = false,
                Children = { List }
            };
            AddChild(new PanelContainer
            {
                Children = { Scroll },
                PanelOverride = new StyleBoxFlat { BackgroundColor = BackgroundColor }
            });
        }

        public void AddToMenu(Control element)
        {
            List.AddChild(new PanelContainer
            {
                Children = { element },
                Margin = new Thickness(MarginSize, MarginSize, MarginSize, MarginSize),
                PanelOverride = new StyleBoxFlat { BackgroundColor = ButtonColor }
            });
        }

        public void RemoveFromMenu(ContextMenuElement element)
        {
            if (element.Parent != null)
            {
                List.RemoveChild(element.Parent);
                InvalidateMeasure();
            }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (List.ChildCount == 0)
            {
                return Vector2.Zero;
            }

            Scroll.Measure(availableSize);
            var size = List.DesiredSize;

            // account for scroll bar width
            if (size.Y > MaxHeight)
            {
                // Scroll._vScrollBar is private and ScrollContainer gives no size information :/
                // 10 = Scroll._vScrollBar.DesiredSize
                size.X += 10;
            }

            return size;
        }
    }
}
