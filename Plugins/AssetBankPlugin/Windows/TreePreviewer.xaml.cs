using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AssetBankPlugin.Ant;
using Frosty.Controls;

namespace AssetBankPlugin.Windows
{
    public partial class TreePreviewer : FrostyWindow
    {
        private enum ViewMode { Local, Global }

        private sealed class GraphNode
        {
            public AntAsset Asset;
            public int Depth;
            public double X, Y;
            public Border Visual;

            public const double W = 220;
            public const double H = 58;
        }

        private sealed class GraphEdge
        {
            public GraphNode From;
            public GraphNode To;
            public string FieldName;
            public Path Visual;
        }

        private readonly AntAsset _root;
        private readonly Guid _rootVmId;

        private readonly Dictionary<Guid, AntAsset> _allById;
        private readonly List<AntAsset> _allAssets;

        private Dictionary<AntAsset, List<(AntAsset referer, string field)>> _reverseByAsset;

        private readonly List<GraphNode> _nodes = new List<GraphNode>();
        private readonly List<GraphEdge> _edges = new List<GraphEdge>();
        private readonly HashSet<(GraphNode, GraphNode, string)> _edgeSet
            = new HashSet<(GraphNode, GraphNode, string)>();
        private readonly Dictionary<Guid, GraphNode> _nodeByCanonId
            = new Dictionary<Guid, GraphNode>();

        private readonly ScaleTransform _scaleT = new ScaleTransform(1, 1);
        private readonly TranslateTransform _translateT = new TranslateTransform(0, 0);

        private GraphNode _draggingNode;
        private Point _dragStartMouse;
        private Point _dragStartNode;
        private bool _isPanning;
        private Point _panStart;
        private ViewMode _currentMode = ViewMode.Global;       
        private int _forwardDepth = 3;
        private int _backwardDepth = 3;

        public Action<AntAsset> AssetDoubleClicked;

        private const double ColSpacing = 300;
        private const double RowSpacing = 90;
        private const double OriginX = 60;
        private const double OriginY = 60;
        private const int LocalFwdDepth = 0;       
        private const int LocalBwdDepth = 1;       
        private const int GlobalNodes = 200;

        public TreePreviewer(AntAsset rootAsset, Guid rootVmId, IEnumerable<AntAssetViewModel> allVms)
        {
            if (rootAsset == null) throw new ArgumentNullException("rootAsset");
            InitializeComponent();

            _root = rootAsset;
            _rootVmId = rootVmId;

            _allById = new Dictionary<Guid, AntAsset>();
            var assetSet = new HashSet<AntAsset>();

            foreach (var vm in allVms)
            {
                if (!(vm.AssetInstance is AntAsset a)) continue;
                assetSet.Add(a);

                if (vm.Id != Guid.Empty && !_allById.ContainsKey(vm.Id))
                    _allById[vm.Id] = a;
                if (a.ID != Guid.Empty && !_allById.ContainsKey(a.ID))
                    _allById[a.ID] = a;
                Guid raw = RawKeyGuid(a);
                if (raw != Guid.Empty && !_allById.ContainsKey(raw))
                    _allById[raw] = a;
            }
            _allAssets = assetSet.ToList();

            PART_RootLabel.Text = rootAsset.Name ?? "Unknown";
            PART_TypeLabel.Text = rootAsset.AssetType ?? string.Empty;
            Title = "Ant Asset Tree  -  " + (rootAsset.Name ?? "Unknown");

            var tg = new TransformGroup();
            tg.Children.Add(_scaleT);
            tg.Children.Add(_translateT);
            PART_Canvas.RenderTransform = tg;

            PART_CanvasHost.MouseRightButtonDown += CanvasHost_RightDown;
            PART_CanvasHost.MouseMove += CanvasHost_MouseMove;
            PART_CanvasHost.MouseRightButtonUp += CanvasHost_RightUp;
            PART_CanvasHost.MouseWheel += CanvasHost_Wheel;
            PART_FitBtn.Click += (s, e) => FitView();
            PART_OrganiseBtn.Click += (s, e) => Organise();
            PART_LocalBtn.Checked += (s, e) => SwitchMode(ViewMode.Local);
            PART_GlobalBtn.Checked += (s, e) => SwitchMode(ViewMode.Global);
            PART_ApplyDepthBtn.Click += (s, e) => ApplyDepthSettings();
            PART_ForwardDepth.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyDepthSettings(); };
            PART_BackwardDepth.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) ApplyDepthSettings(); };

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            ShowLoading("Building index...");
            await System.Threading.Tasks.Task.Run(() => BuildReverseIndex());
            await RebuildAsync();
        }

        private void ShowLoading(string message)
        {
            if (message == null)
            {
                PART_LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                PART_LoadingText.Text = message;
                PART_LoadingOverlay.Visibility = Visibility.Visible;
            }
        }

        private void BuildReverseIndex()
        {
            _reverseByAsset = new Dictionary<AntAsset, List<(AntAsset, string)>>();

            foreach (var asset in _allAssets)
            {
                if (asset.RawData == null) continue;
                var refs = new List<(string field, Guid id)>();
                CollectRefs(asset.RawData, refs, topLevel: true);

                foreach (var (field, id) in refs)
                {
                    AntAsset target;
                    if (!_allById.TryGetValue(id, out target) || target == asset) continue;

                    List<(AntAsset, string)> lst;
                    if (!_reverseByAsset.TryGetValue(target, out lst))
                        _reverseByAsset[target] = lst = new List<(AntAsset, string)>();
                    lst.Add((asset, field));
                }
            }
        }

        private void SwitchMode(ViewMode mode)
        {
            if (_currentMode == mode) return;
            _currentMode = mode;
            PART_DepthPanel.Visibility = mode == ViewMode.Global
                ? Visibility.Visible
                : Visibility.Collapsed;
            Rebuild();
        }

        private void ApplyDepthSettings()
        {
            int fwd, bwd;
            if (!int.TryParse(PART_ForwardDepth.Text.Trim(), out fwd) || fwd < 0) fwd = 3;
            if (!int.TryParse(PART_BackwardDepth.Text.Trim(), out bwd) || bwd < 0) bwd = 3;
            _forwardDepth = Math.Min(fwd, 20);
            _backwardDepth = Math.Min(bwd, 20);
            PART_ForwardDepth.Text = _forwardDepth.ToString();
            PART_BackwardDepth.Text = _backwardDepth.ToString();
            Rebuild();
        }

        private void Rebuild()
        {
#pragma warning disable CS4014
            RebuildAsync();
#pragma warning restore CS4014
        }

        private async System.Threading.Tasks.Task RebuildAsync()
        {
            ShowLoading("Building graph...");
            PART_Canvas.Children.Clear();
            _nodes.Clear(); _edges.Clear(); _edgeSet.Clear(); _nodeByCanonId.Clear();

            int fwdDepth = _currentMode == ViewMode.Local ? LocalFwdDepth : _forwardDepth;
            int bwdDepth = _currentMode == ViewMode.Local ? LocalBwdDepth : _backwardDepth;
            int nodeLimit = _currentMode == ViewMode.Local ? int.MaxValue : GlobalNodes;

            await System.Threading.Tasks.Task.Run(
                () => BuildBidirectionalGraph(fwdDepth, bwdDepth, nodeLimit));

            LayoutNodes();
            RenderGraph();
            FitView();
            ShowLoading(null);
        }

        private void BuildBidirectionalGraph(int fwdDepth, int bwdDepth, int nodeLimit)
        {
            var rootNode = RegisterNode(_root, _rootVmId, 0);

            var outQueue = new Queue<(GraphNode node, int depth)>();
            outQueue.Enqueue((rootNode, 0));

            while (outQueue.Count > 0 && _nodes.Count < nodeLimit)
            {
                var (parentNode, depth) = outQueue.Dequeue();
                if (depth >= bwdDepth || parentNode.Asset.RawData == null) continue;

                var refs = new List<(string field, Guid id)>();
                CollectRefs(parentNode.Asset.RawData, refs, topLevel: true);

                foreach (var (field, id) in refs)
                {
                    AntAsset target;
                    if (!_allById.TryGetValue(id, out target) || target == parentNode.Asset) continue;

                    var targetNode = FindNode(id);
                    if (targetNode == null)
                    {
                        targetNode = RegisterNode(target, id, depth + 1);
                        outQueue.Enqueue((targetNode, depth + 1));
                    }
                    AddEdge(parentNode, targetNode, field);
                }
            }

            var inQueue = new Queue<(GraphNode node, int depth)>();
            inQueue.Enqueue((rootNode, 0));

            while (inQueue.Count > 0 && _nodes.Count < nodeLimit)
            {
                var (node, depth) = inQueue.Dequeue();
                if (depth <= -fwdDepth) continue;               

                List<(AntAsset referer, string field)> referers;
                if (!_reverseByAsset.TryGetValue(node.Asset, out referers)) continue;

                foreach (var (referer, field) in referers)
                {
                    if (_nodes.Count >= nodeLimit) break;

                    var existing = FindNode(referer.ID) ?? FindNode(RawKeyGuid(referer));
                    if (existing != null)
                    {
                        AddEdge(existing, node, field);
                        continue;
                    }

                    var srcNode = RegisterNode(referer, referer.ID, depth - 1);
                    AddEdge(srcNode, node, field);
                    inQueue.Enqueue((srcNode, depth - 1));
                }
            }
        }

        private GraphNode RegisterNode(AntAsset asset, Guid primaryId, int depth)
        {
            var node = new GraphNode { Asset = asset, Depth = depth };
            _nodes.Add(node);

            if (primaryId != Guid.Empty) _nodeByCanonId[primaryId] = node;
            if (asset.ID != Guid.Empty) _nodeByCanonId[asset.ID] = node;
            Guid raw = RawKeyGuid(asset);
            if (raw != Guid.Empty) _nodeByCanonId[raw] = node;
            return node;
        }

        private GraphNode FindNode(Guid id)
        {
            GraphNode n;
            return (id != Guid.Empty && _nodeByCanonId.TryGetValue(id, out n)) ? n : null;
        }

        private void AddEdge(GraphNode from, GraphNode to, string field)
        {
            if (from == null || to == null || from == to) return;
            if (_edgeSet.Add((from, to, field)))
                _edges.Add(new GraphEdge { From = from, To = to, FieldName = field });
        }

        private static Guid RawKeyGuid(AntAsset asset)
        {
            if (asset.RawData == null) return Guid.Empty;
            object k;
            if (asset.RawData.TryGetValue("__key", out k) ||
                asset.RawData.TryGetValue("__guid", out k))
                return TryExtractGuid(k);
            return Guid.Empty;
        }

        private static Guid TryExtractGuid(object val)
        {
            if (val == null) return Guid.Empty;
            if (val is Guid g) return g;
            if (val is string s)
            {
                if (s.Length > 0 && s.Length <= 16 &&
                    ulong.TryParse(s, NumberStyles.HexNumber, null, out ulong u) && u != 0)
                {
                    byte[] b = new byte[16];
                    BitConverter.GetBytes(u).CopyTo(b, 0);
                    return new Guid(b);
                }
                if (Guid.TryParse(s, out Guid r)) return r;
                return Guid.Empty;
            }
            if (val is ulong ul && ul != 0)
            {
                byte[] b = new byte[16];
                BitConverter.GetBytes(ul).CopyTo(b, 0);
                return new Guid(b);
            }
            if (val is long l && l > 0)
            {
                byte[] b = new byte[16];
                BitConverter.GetBytes(l).CopyTo(b, 0);
                return new Guid(b);
            }
            return Guid.Empty;
        }

        private static readonly HashSet<string> s_skipKeys = new HashSet<string>
        {
            "__name", "__guid", "__key", "__base", "__typeHash"
        };

        private static void CollectRefs(
            Dictionary<string, object> data,
            List<(string, Guid)> refs,
            bool topLevel,
            string prefix = "")
        {
            if (data == null) return;
            foreach (var kvp in data)
            {
                var key = kvp.Key;
                if (s_skipKeys.Contains(key)) continue;        

                var val = kvp.Value;
                if (val == null) continue;

                string fullKey = prefix.Length == 0 ? key : prefix + "." + key;

                var g = TryExtractGuid(val);
                if (g != Guid.Empty) { refs.Add((fullKey, g)); continue; }

                if (val is Dictionary<string, object> nested)
                {
                    CollectRefs(nested, refs, topLevel: false, prefix: fullKey);
                }
                else if (val is object[] arr)
                {
                    CollectRefsArray(arr, refs, fullKey);
                }
            }
        }

        private static void CollectRefsArray(object[] arr, List<(string, Guid)> refs, string prefix)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var item = arr[i];
                if (item == null) continue;
                string itemKey = prefix + "[" + i + "]";

                var ag = TryExtractGuid(item);
                if (ag != Guid.Empty) { refs.Add((itemKey, ag)); continue; }

                if (item is Dictionary<string, object> d)
                    CollectRefs(d, refs, topLevel: false, prefix: itemKey);
                else if (item is object[] inner)
                    CollectRefsArray(inner, refs, itemKey);
            }
        }

        private void LayoutNodes()
        {
            if (_nodes.Count == 0) return;

            var inN = new Dictionary<GraphNode, List<GraphNode>>();
            var outN = new Dictionary<GraphNode, List<GraphNode>>();
            foreach (var e in _edges)
            {
                List<GraphNode> il;
                if (!inN.TryGetValue(e.To, out il)) inN[e.To] = il = new List<GraphNode>();
                il.Add(e.From);
                List<GraphNode> ol;
                if (!outN.TryGetValue(e.From, out ol)) outN[e.From] = ol = new List<GraphNode>();
                ol.Add(e.To);
            }

            int minD = _nodes.Min(n => n.Depth);
            int maxD = _nodes.Max(n => n.Depth);
            foreach (var n in _nodes) n.X = OriginX + (n.Depth - minD) * ColSpacing;

            var byDepth = _nodes.GroupBy(n => n.Depth).ToDictionary(g => g.Key, g => g.ToList());

            List<GraphNode> rootCol;
            if (byDepth.TryGetValue(0, out rootCol)) PlaceColumn(rootCol, null);

            for (int d = 1; d <= maxD; d++)
            {
                List<GraphNode> col;
                if (byDepth.TryGetValue(d, out col)) PlaceColumn(col, inN);
            }

            for (int d = -1; d >= minD; d--)
            {
                List<GraphNode> col;
                if (byDepth.TryGetValue(d, out col)) PlaceColumn(col, outN);
            }
        }

        private void PlaceColumn(List<GraphNode> col, Dictionary<GraphNode, List<GraphNode>> towardRoot)
        {
            int n = col.Count;
            var desired = new double[n];
            for (int i = 0; i < n; i++)
            {
                List<GraphNode> nb;
                if (towardRoot != null && towardRoot.TryGetValue(col[i], out nb) && nb.Count > 0)
                {
                    double sum = 0;
                    foreach (var x in nb) sum += x.Y;
                    desired[i] = sum / nb.Count;
                }
                else desired[i] = 0;
            }

            var order = Enumerable.Range(0, n).OrderBy(i => desired[i]).ToList();

            double prev = double.NegativeInfinity;
            foreach (var i in order)
            {
                double y = desired[i];
                if (y < prev + RowSpacing) y = prev + RowSpacing;
                col[i].Y = y;
                prev = y;
            }

            double meanDesired = 0, meanActual = 0;
            for (int i = 0; i < n; i++) { meanDesired += desired[i]; meanActual += col[i].Y; }
            if (n > 0)
            {
                double shift = (meanDesired - meanActual) / n;
                foreach (var node in col) node.Y += shift;
            }
        }

        private void RenderGraph()
        {
            PART_Canvas.Children.Clear();
            foreach (var edge in _edges)
            {
                edge.Visual = BuildEdgePath(edge);
                PART_Canvas.Children.Add(edge.Visual);
            }
            foreach (var node in _nodes)
            {
                node.Visual = BuildNodeBorder(node);
                Canvas.SetLeft(node.Visual, node.X);
                Canvas.SetTop(node.Visual, node.Y);
                PART_Canvas.Children.Add(node.Visual);
            }
        }

        private Border BuildNodeBorder(GraphNode node)
        {
            bool isRoot = node.Asset == _root;

            var headerBg = new SolidColorBrush(isRoot
                ? Color.FromRgb(0x0F, 0x3A, 0x56)
                : Color.FromRgb(0x2A, 0x2A, 0x2D));
            var borderClr = new SolidColorBrush(isRoot
                ? Color.FromRgb(0x00, 0x7A, 0xCC)
                : Color.FromRgb(0x3F, 0x3F, 0x46));

            var typeLabel = new TextBlock
            {
                Text = node.Asset.AssetType ?? "Unknown",
                Foreground = new SolidColorBrush(isRoot
                                        ? Color.FromRgb(0x80, 0xC8, 0xFF)
                                        : Color.FromRgb(0x80, 0x80, 0x80)),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            TextOptions.SetTextFormattingMode(typeLabel, TextFormattingMode.Display);

            var nameLabel = new TextBlock
            {
                Text = node.Asset.Name ?? "Unknown",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 6, 8, 6),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            TextOptions.SetTextFormattingMode(nameLabel, TextFormattingMode.Display);

            var body = new StackPanel();
            body.Children.Add(new Border
            {
                Background = headerBg,
                Height = 22,
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                Child = typeLabel,
            });
            body.Children.Add(nameLabel);

            var ttText = new TextBlock
            {
                Text = (node.Asset.AssetType ?? "?") + "  |  " + node.Asset.ID,
                Foreground = Brushes.White,
                FontSize = 11,
            };
            TextOptions.SetTextFormattingMode(ttText, TextFormattingMode.Display);

            var border = new Border
            {
                Width = GraphNode.W,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                BorderBrush = borderClr,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = body,
                Cursor = Cursors.SizeAll,
                ToolTip = new ToolTip
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 4, 7, 4),
                    HasDropShadow = true,
                    Content = ttText,
                },
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    AssetDoubleClicked?.Invoke(node.Asset);
                    e.Handled = true;
                }
                else
                {
                    NodeDragStart(node, e);
                }
            };
            border.MouseMove += (s, e) => NodeDragMove(node, e);
            border.MouseLeftButtonUp += (s, e) => NodeDragEnd(node, e);

            var copyGuidItem = new MenuItem { Header = "Copy GUID" };
            copyGuidItem.Click += (s, e) =>
            {
                try { Clipboard.SetText(node.Asset.ID.ToString()); } catch { }
            };
            var ctxMenu = new ContextMenu();
            ctxMenu.Items.Add(copyGuidItem);

            object rawKeyObj;
            if (node.Asset.RawData != null &&
                (node.Asset.RawData.TryGetValue("__key", out rawKeyObj) ||
                 node.Asset.RawData.TryGetValue("__guid", out rawKeyObj)) &&
                rawKeyObj != null)
            {
                var rawStr = rawKeyObj.ToString();
                if (rawStr != "0000000000000000" && rawStr != string.Empty)
                {
                    var copyKeyItem = new MenuItem
                    {
                        Header = "Copy Key  (" + rawStr + ")"
                    };
                    copyKeyItem.Click += (s, e) =>
                    {
                        try { Clipboard.SetText(rawStr); } catch { }
                    };
                    ctxMenu.Items.Add(copyKeyItem);
                }
            }
            border.ContextMenu = ctxMenu;
            return border;
        }

        private Path BuildEdgePath(GraphEdge edge)
        {
            var path = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                StrokeThickness = 1.5,
                Opacity = 0.7,
                IsHitTestVisible = false,
            };
            RefreshEdgePath(edge.From, edge.To, path);
            return path;
        }

        private static void RefreshEdgePath(GraphNode from, GraphNode to, Path path)
        {
            double x1 = from.X + GraphNode.W;
            double y1 = from.Y + GraphNode.H / 2.0;
            double x2 = to.X;
            double y2 = to.Y + GraphNode.H / 2.0;
            double cp = Math.Max(60, Math.Abs(x2 - x1) * 0.45);
            var fig = new PathFigure { StartPoint = new Point(x1, y1), IsFilled = false };
            fig.Segments.Add(new BezierSegment(
                new Point(x1 + cp, y1), new Point(x2 - cp, y2), new Point(x2, y2),
                isStroked: true));
            path.Data = new PathGeometry(new[] { fig });
        }

        private void UpdateEdgesForNode(GraphNode node)
        {
            foreach (var edge in _edges)
                if (edge.From == node || edge.To == node)
                    RefreshEdgePath(edge.From, edge.To, edge.Visual);
        }

        private void NodeDragStart(GraphNode node, MouseButtonEventArgs e)
        {
            _draggingNode = node;
            _dragStartMouse = e.GetPosition(PART_CanvasHost);
            _dragStartNode = new Point(node.X, node.Y);
            node.Visual.CaptureMouse();
            e.Handled = true;
        }

        private void NodeDragMove(GraphNode node, MouseEventArgs e)
        {
            if (_draggingNode != node || !node.Visual.IsMouseCaptured) return;
            var pos = e.GetPosition(PART_CanvasHost);
            node.X = _dragStartNode.X + (pos.X - _dragStartMouse.X) / _scaleT.ScaleX;
            node.Y = _dragStartNode.Y + (pos.Y - _dragStartMouse.Y) / _scaleT.ScaleY;
            Canvas.SetLeft(node.Visual, node.X);
            Canvas.SetTop(node.Visual, node.Y);
            UpdateEdgesForNode(node);
        }

        private void NodeDragEnd(GraphNode node, MouseButtonEventArgs e)
        {
            if (_draggingNode != node) return;
            _draggingNode = null;
            node.Visual.ReleaseMouseCapture();
        }

        private void CanvasHost_RightDown(object sender, MouseButtonEventArgs e)
        {
            _isPanning = true;
            _panStart = e.GetPosition(PART_CanvasHost);
            PART_CanvasHost.CaptureMouse();
            e.Handled = true;
        }

        private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;
            var pos = e.GetPosition(PART_CanvasHost);
            _translateT.X += pos.X - _panStart.X;
            _translateT.Y += pos.Y - _panStart.Y;
            _panStart = pos;
        }

        private void CanvasHost_RightUp(object sender, MouseButtonEventArgs e)
        {
            _isPanning = false;
            PART_CanvasHost.ReleaseMouseCapture();
        }

        private void CanvasHost_Wheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = Math.Max(0.15, Math.Min(4.0, _scaleT.ScaleX * factor));
            double ratio = newScale / _scaleT.ScaleX;
            var mouse = e.GetPosition(PART_CanvasHost);
            _translateT.X = mouse.X - (mouse.X - _translateT.X) * ratio;
            _translateT.Y = mouse.Y - (mouse.Y - _translateT.Y) * ratio;
            _scaleT.ScaleX = newScale;
            _scaleT.ScaleY = newScale;
        }

        private void Organise()
        {
            LayoutNodes();
            foreach (var node in _nodes)
            {
                if (node.Visual == null) continue;
                Canvas.SetLeft(node.Visual, node.X);
                Canvas.SetTop(node.Visual, node.Y);
            }
            foreach (var edge in _edges)
                if (edge.Visual != null)
                    RefreshEdgePath(edge.From, edge.To, edge.Visual);
            FitView();
        }

        private void FitView()
        {
            if (_nodes.Count == 0) return;
            double minX = _nodes.Min(n => n.X);
            double minY = _nodes.Min(n => n.Y);
            double maxX = _nodes.Max(n => n.X) + GraphNode.W;
            double maxY = _nodes.Max(n => n.Y) + GraphNode.H;
            const double pad = 40;
            double gw = maxX - minX + pad * 2;
            double gh = maxY - minY + pad * 2;
            double hostW = PART_CanvasHost.ActualWidth;
            double hostH = PART_CanvasHost.ActualHeight;
            if (hostW < 1 || hostH < 1) return;
            double scale = Math.Min(1.0, Math.Min(hostW / gw, hostH / gh));
            _scaleT.ScaleX = scale;
            _scaleT.ScaleY = scale;
            _translateT.X = (hostW - gw * scale) / 2.0 - (minX - pad) * scale;
            _translateT.Y = (hostH - gh * scale) / 2.0 - (minY - pad) * scale;
        }
    }
}