﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace SetsView
{
    using System;
    using System.Linq;
    using Microsoft.Toolkit.Uwp.UI.Extensions;
    using Windows.ApplicationModel.DataTransfer;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Controls.Primitives;
    using Windows.UI.Xaml.Data;
    using Windows.UI.Xaml.Input;

    /// <summary>
    /// SetsView is a control for displaying a set of sets and their content.
    /// </summary>
    [TemplatePart(Name = SetsContentPresenterName, Type = typeof(ContentPresenter))]
    [TemplatePart(Name = SetsViewContainerName, Type = typeof(Grid))]
    [TemplatePart(Name = SetsItemsPresenterName, Type = typeof(ItemsPresenter))]
    [TemplatePart(Name = SetsScrollViewerName, Type = typeof(ScrollViewer))]
    [TemplatePart(Name = SetsScrollBackButtonName, Type = typeof(ButtonBase))]
    [TemplatePart(Name = SetsScrollForwardButtonName, Type = typeof(ButtonBase))]
    public partial class SetsView : ListViewBase
    {
        private const int ScrollAmount = 50; // TODO: Should this be based on SetsWidthMode

        private const string SetsContentPresenterName = "SetsContentPresenter";
        private const string SetsViewContainerName = "SetsViewContainer";
        private const string SetsItemsPresenterName = "SetsItemsPresenter";
        private const string SetsScrollViewerName = "ScrollViewer";
        private const string SetsScrollBackButtonName = "SetsScrollBackButton";
        private const string SetsScrollForwardButtonName = "SetsScrollForwardButton";

        private ContentPresenter _setsContentPresenter;
        private Grid _setsViewContainer;
        private ItemsPresenter _setItemsPresenter;
        private ScrollViewer _setsScroller;
        private ButtonBase _setsScrollBackButton;
        private ButtonBase _setsScrollForwardButton;

        private bool _hasLoaded;
        private bool _isDragging;

        private double _scrollViewerHorizontalOffset = .0f;
        public double ScrollViewerHorizontalOffset => _scrollViewerHorizontalOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetsView"/> class.
        /// </summary>
        public SetsView()
        {
            DefaultStyleKey = typeof(SetsView);

            // Container Generation Hooks
            RegisterPropertyChangedCallback(ItemsSourceProperty, ItemsSource_PropertyChanged);
            ItemContainerGenerator.ItemsChanged += ItemContainerGenerator_ItemsChanged;

            // Drag and Layout Hooks
            DragItemsStarting += SetsView_DragItemsStarting;
            DragItemsCompleted += SetsView_DragItemsCompleted;
            SizeChanged += SetsView_SizeChanged;

            // Selection Hook
            SelectionChanged += SetsView_SelectionChanged;
        }

        /// <summary>
        /// Occurs when a set is dragged by the user outside of the <see cref="SetsView"/>.  Generally, this paradigm is used to create a new-window with the torn-off set.
        /// The creation and handling of the new-window is left to the app's developer.
        /// </summary>
        public event EventHandler<SetDraggedOutsideEventArgs> SetDraggedOutside;

        /// <summary>
        /// Occurs when a set's Close button is clicked.  Set <see cref="SetClosingEventArgs.Cancel"/> to true to prevent automatic Set Closure.
        /// </summary>
        public event EventHandler<SetClosingEventArgs> SetClosing;

        /// <summary>
        /// Occurs when a set is selected in <see cref="SetsView"/>.
        /// </summary>
        public event EventHandler<SetSelectedEventArgs> SetSelected;

        /// <summary>
        /// Occurs when a set is tapped in <see cref="SetsView"/>.
        /// </summary>
        public event EventHandler<SetSelectedEventArgs> SetTapped;

        /// <summary>
        /// Occurs when a set is double tapped in <see cref="SetsView"/>.
        /// </summary>
        public event EventHandler<SetSelectedEventArgs> SetDoubleTapped;

        /// <inheritdoc/>
        protected override DependencyObject GetContainerForItemOverride() => new SetsViewItem();

        /// <inheritdoc/>
        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is SetsViewItem;
        }

        /// <inheritdoc/>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_setItemsPresenter != null)
            {
                _setItemsPresenter.SizeChanged -= SetsView_SizeChanged;
            }

            if (_setsScroller != null)
            {
                _setsScroller.Loaded -= SetsScrollViewer_Loaded;
                _setsScroller.ViewChanged -= SetsScrollViewer_ViewChanged;
            }

            _setsContentPresenter = GetTemplateChild(SetsContentPresenterName) as ContentPresenter;
            _setsViewContainer = GetTemplateChild(SetsViewContainerName) as Grid;
            _setItemsPresenter = GetTemplateChild(SetsItemsPresenterName) as ItemsPresenter;
            _setsScroller = GetTemplateChild(SetsScrollViewerName) as ScrollViewer;

            if (_setItemsPresenter != null)
            {
                _setItemsPresenter.SizeChanged += SetsView_SizeChanged;
            }

            if (_setsScroller != null)
            {
                _setsScroller.Loaded += SetsScrollViewer_Loaded;
                _setsScroller.ViewChanged += SetsScrollViewer_ViewChanged;
            }
        }

        private void SetsScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            _setsScroller.Loaded -= SetsScrollViewer_Loaded;

            if (_setsScrollBackButton != null)
            {
                _setsScrollBackButton.Click -= ScrollSetBackButton_Click;
            }

            if (_setsScrollForwardButton != null)
            {
                _setsScrollForwardButton.Click -= ScrollSetForwardButton_Click;
            }

            _setsScrollBackButton = _setsScroller.FindDescendantByName(SetsScrollBackButtonName) as ButtonBase;
            _setsScrollForwardButton = _setsScroller.FindDescendantByName(SetsScrollForwardButtonName) as ButtonBase;

            if (_setsScrollBackButton != null)
            {
                _setsScrollBackButton.Click += ScrollSetBackButton_Click;
            }

            if (_setsScrollForwardButton != null)
            {
                _setsScrollForwardButton.Click += ScrollSetForwardButton_Click;
            }
        }

        private void ScrollSetBackButton_Click(object sender, RoutedEventArgs e)
        {
            _setsScroller.ChangeView(Math.Max(0, _setsScroller.HorizontalOffset - ScrollAmount), null, null);
        }

        private void ScrollSetForwardButton_Click(object sender, RoutedEventArgs e)
        {
            _setsScroller.ChangeView(Math.Min(_setsScroller.ScrollableWidth, _setsScroller.HorizontalOffset + ScrollAmount), null, null);
        }

        private void SetsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isDragging)
            {
                // Skip if we're dragging, we'll reset when we're done.
                return;
            }

            if (_setsContentPresenter != null)
            {
                if (SelectedItem == null)
                {
                    _setsContentPresenter.Content = null;
                    _setsContentPresenter.ContentTemplate = null;
                }
                else
                {
                    if (ContainerFromItem(SelectedItem) is SetsViewItem container)
                    {
                        _setsContentPresenter.Content = container.Content;
                        _setsContentPresenter.ContentTemplate = container.ContentTemplate;

                        if (e != null) _setsContentPresenter.Loaded += SetsContentPresenter_Loaded;
                    }
                }
            }

            // If our width can be effected by the selection, need to run algorithm.
            if (!double.IsNaN(SelectedSetWidth))
            {
                SetsView_SizeChanged(sender, null);
            }
        }

        private void SetsContentPresenter_Loaded(object sender, RoutedEventArgs e)
        {
            _setsContentPresenter.Loaded -= SetsContentPresenter_Loaded;
            var args = new SetSelectedEventArgs(((SetsViewItem)ContainerFromItem(SelectedItem))?.Content, (SetsViewItem)ContainerFromItem(SelectedItem));
            SetSelected?.Invoke(this, args);
        }

        /// <inheritdoc/>
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);

            var setItem = element as SetsViewItem;

            setItem.Loaded -= SetsViewItem_Loaded;
            setItem.Closing -= SetsViewItem_Closing;
            setItem.Tapped -= SetsViewItem_Tapped;
            setItem.DoubleTapped -= SetsViewItem_DoubleTapped;
            setItem.Loaded += SetsViewItem_Loaded;
            setItem.Closing += SetsViewItem_Closing;
            setItem.Tapped += SetsViewItem_Tapped;
            setItem.DoubleTapped += SetsViewItem_DoubleTapped;

            if (setItem.Header == null)
            {
                setItem.Header = item;
            }

            if (setItem.HeaderTemplate == null)
            {
                var headertemplatebinding = new Binding()
                {
                    Source = this,
                    Path = new PropertyPath(nameof(ItemHeaderTemplate)),
                    Mode = BindingMode.OneWay
                };
                setItem.SetBinding(SetsViewItem.HeaderTemplateProperty, headertemplatebinding);
            }

            if (setItem.IsClosable != true && setItem.ReadLocalValue(SetsViewItem.IsClosableProperty) == DependencyProperty.UnsetValue)
            {
                var iscloseablebinding = new Binding()
                {
                    Source = this,
                    Path = new PropertyPath(nameof(CanCloseSets)),
                    Mode = BindingMode.OneWay,
                };
                setItem.SetBinding(SetsViewItem.IsClosableProperty, iscloseablebinding);
            }
        }

        private void SetsViewItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            var args = new SetSelectedEventArgs(((SetsViewItem)ContainerFromItem(SelectedItem))?.Content, (SetsViewItem)ContainerFromItem(SelectedItem));
            SetDoubleTapped?.Invoke(sender, args);
        }

        private void SetsViewItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var args = new SetSelectedEventArgs(((SetsViewItem)ContainerFromItem(SelectedItem))?.Content, (SetsViewItem)ContainerFromItem(SelectedItem));
            SetTapped?.Invoke(sender, args);
        }

        private void SetsViewItem_Loaded(object sender, RoutedEventArgs e)
        {
            var setItem = sender as SetsViewItem;

            setItem.Loaded -= SetsViewItem_Loaded;

            // Only need to do this once.
            if (!_hasLoaded)
            {
                _hasLoaded = true;

                // Need to set a set's selection on load, otherwise ListView resets to null.
                SetInitialSelection();

                // Need to make sure ContentPresenter is set to content based on selection.
                SetsView_SelectionChanged(this, null);

                // Need to make sure we've registered our removal method.
                ItemsSource_PropertyChanged(this, null);

                // Make sure we complete layout now.
                SetsView_SizeChanged(this, null);
            }
        }

        private void SetsViewItem_Closing(object sender, SetClosingEventArgs e)
        {
            var item = ItemFromContainer(e.Set);

            var args = new SetClosingEventArgs(item, e.Set);
            SetClosing?.Invoke(this, args);

            if (!args.Cancel)
            {
                if (ItemsSource != null)
                {
                    _removeItemsSourceMethod?.Invoke(ItemsSource, new object[] { item });
                }
                else
                {
                    Items.Remove(item);
                }
            }
        }

        private void SetsView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            // Keep track of drag so we don't modify content until done.
            _isDragging = true;
        }

        private void SetsView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            _isDragging = false;

            // args.DropResult == None when outside of area (e.g. create new window)
            if (args.DropResult == DataPackageOperation.None)
            {
                var item = args.Items.FirstOrDefault();
                var set = ContainerFromItem(item) as SetsViewItem;

                if (set == null && item is FrameworkElement fe)
                {
                    set = fe.FindParent<SetsViewItem>();
                }

                if (set == null)
                {
                    // We still don't have a SetsViewItem, most likely is a static SetsViewItem in the template being dragged and not selected.
                    // This is a fallback scenario for static sets.
                    // Note: This can be wrong if two SetsViewItems share the exact same Content (i.e. a string), this should be unlikely in any practical scenario.
                    for (int i = 0; i < Items.Count; i++)
                    {
                        var setItem = ContainerFromIndex(i) as SetsViewItem;
                        if (ReferenceEquals(setItem.Content, item))
                        {
                            set = setItem;
                            break;
                        }
                    }
                }

                SetDraggedOutside?.Invoke(this, new SetDraggedOutsideEventArgs(item, set));
            }
            else
            {
                // If dragging the active set, there's an issue with the CP blanking.
                SetsView_SelectionChanged(this, null);
            }
        }

        private void SetsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            _scrollViewerHorizontalOffset = _setsScroller.HorizontalOffset;
        }

        public void ScrollToLastSet()
        {
            _setsScroller.UpdateLayout();
            _setsScroller.ChangeView(double.MaxValue, 0.0f, 1.0f);
        }

        public void ScrollTo(double offset)
        {
            _setsScroller.UpdateLayout();
            _setsScroller.ChangeView(offset, 0.0f, 1.0f);
        }
    }
}
