// Taj's COI Mods | TajsWorldOperationsWindow.cs

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Economy;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.World;
using Mafi.Core.World.Entities;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using UnityEngine.UIElements;
using Column = Mafi.Unity.UiToolkit.Library.Column;
using Label = Mafi.Unity.UiToolkit.Library.Label;
using TextField = Mafi.Unity.UiToolkit.Library.TextField;
using Button = Mafi.Unity.UiToolkit.Library.Button;

namespace TajsCOI.Tweaks
{
    /// <summary>
    ///     Native world-map management window. All actions remain ordinary world-map input
    ///     commands; the window only enumerates current entities and presents their progress.
    /// </summary>
    internal sealed class TajsWorldOperationsWindow : Window
    {
        private readonly WorldMapManager m_worldMap;
        private readonly IInputScheduler m_inputScheduler;
        private readonly IProductsManager m_productsManager;
        private readonly ScrollColumn m_repairsScroll;
        private readonly ScrollColumn m_minesScroll;
        private readonly ScrollColumn m_settlementsScroll;
        private readonly ScrollColumn m_preloadScroll;
        private readonly Column m_repairsContent;
        private readonly Column m_minesContent;
        private readonly Column m_settlementsContent;
        private readonly Column m_preloadContent;
        private readonly Label m_status;
        private readonly ButtonText m_repairsTab;
        private readonly ButtonText m_minesTab;
        private readonly ButtonText m_settlementsTab;
        private readonly ButtonText m_preloadTab;
        private readonly Label m_preloadFeedback;
        private readonly Column m_preloadPending;
        private readonly Column m_preloadCargo;
        private SingleProductPickerUi? m_productPicker;
        private TextField? m_quantity;
        private ProductProto? m_selectedProduct;
        private int m_activeTab;

        private static readonly Color EvenRow = new(10f / 51f, 11f / 51f, 0.23529412f, 0.7058824f);
        private static readonly Color OddRow = new(13f / 51f, 24f / 85f, 26f / 85f, 0.7058824f);

        internal TajsWorldOperationsWindow(
            WorldMapManager worldMap,
            IInputScheduler inputScheduler,
            IProductsManager productsManager,
            UiRoot uiRoot)
            : base("World operations".AsLoc())
        {
            m_worldMap = worldMap;
            m_inputScheduler = inputScheduler;
            m_productsManager = productsManager;

            WindowSize(new Px(900f), new Px(620f));
            Panel panel = new Panel().BodyGap(new Px(4f));
            var tabs = new Row(new Px(4f));
            m_repairsTab = MakeTab("Repairs", 0, "Unrepaired world-map entities.");
            m_minesTab = MakeTab("Mines & rigs", 1, "Repaired mines and oil rigs.");
            m_settlementsTab = MakeTab("Settlements", 2, "Settlements and reputation upgrades.");
            m_preloadTab = MakeTab("Ship preload", 3, "Queue cargo through ordinary shipyard logistics.");
            tabs.Add(m_repairsTab);
            tabs.Add(m_minesTab);
            tabs.Add(m_settlementsTab);
            tabs.Add(m_preloadTab);
            panel.Add(tabs);

            m_repairsContent = new Column(2.pt()).AlignItemsStretch();
            m_minesContent = new Column(2.pt()).AlignItemsStretch();
            m_settlementsContent = new Column(2.pt()).AlignItemsStretch();
            m_preloadContent = new Column(6.pt()).AlignItemsStretch();
            m_repairsScroll = MakeScroll(m_repairsContent);
            m_minesScroll = MakeScroll(m_minesContent);
            m_settlementsScroll = MakeScroll(m_settlementsContent);
            m_preloadScroll = MakeScroll(m_preloadContent);
            panel.Add(m_repairsScroll);
            panel.Add(m_minesScroll);
            panel.Add(m_settlementsScroll);
            panel.Add(m_preloadScroll);
            m_status = new Label(string.Empty.AsLoc());
            panel.Add(m_status);
            Body.Add(panel);

            m_preloadFeedback = new Label(string.Empty.AsLoc());
            m_preloadPending = new Column(2.pt()).AlignItemsStretch();
            m_preloadCargo = new Column(2.pt()).AlignItemsStretch();
            BuildPreloadPanel();
            SwitchTab(0);
            RootElement.schedule.Execute(RefreshAll).Every(2000L);
            RefreshAll();
            MakeMovableAndEnablePositionSaving();
            CloseOnClickOutside();
            Open(uiRoot);
        }

        internal void SwitchToPreloadTab() => SwitchTab(3);

        private ButtonText MakeTab(string text, int tab, string tooltip)
        {
            var button = new ButtonText(Button.General, text.AsLoc(), () => SwitchTab(tab));
            button.Width(new Px(215f));
            button.Tooltip(tooltip.AsLoc());
            button.RootElement.style.flexShrink = 0f;
            return button;
        }

        private static ScrollColumn MakeScroll(Column content)
        {
            var scroll = new ScrollColumn();
            scroll.Add(content);
            return scroll;
        }

        private void SwitchTab(int tab)
        {
            m_activeTab = tab;
            m_repairsScroll.RootElement.style.display = tab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_minesScroll.RootElement.style.display = tab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            m_settlementsScroll.RootElement.style.display = tab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            m_preloadScroll.RootElement.style.display = tab == 3 ? DisplayStyle.Flex : DisplayStyle.None;
            m_repairsTab.RootElement.style.backgroundColor = new StyleColor(tab == 0 ? EvenRow : OddRow);
            m_minesTab.RootElement.style.backgroundColor = new StyleColor(tab == 1 ? EvenRow : OddRow);
            m_settlementsTab.RootElement.style.backgroundColor = new StyleColor(tab == 2 ? EvenRow : OddRow);
            m_preloadTab.RootElement.style.backgroundColor = new StyleColor(tab == 3 ? EvenRow : OddRow);
            RefreshAll();
        }

        private void RefreshAll()
        {
            try
            {
                m_repairsContent.Clear();
                m_minesContent.Clear();
                m_settlementsContent.Clear();
                int repairs = 0;
                int mines = 0;
                int settlements = 0;
                foreach (WorldMapLocation location in m_worldMap.Map.Locations)
                {
                    if (location.State != WorldMapLocationState.Explored || location.Entity.IsNone)
                    {
                        continue;
                    }

                    IWorldMapEntity entity = location.Entity.Value;
                    if (entity is WorldMapVillage village)
                    {
                        if (!village.IsRepaired || village.IsUnderConstruction)
                        {
                            m_repairsContent.Add(BuildRepairRow(village, repairs++));
                        }
                        m_settlementsContent.Add(BuildSettlementRow(village, settlements++));
                    }
                    else if (entity is WorldMapMine mine)
                    {
                        if (!mine.IsRepaired || mine.IsUnderConstruction)
                        {
                            m_repairsContent.Add(BuildRepairRow(mine, repairs++));
                        }
                        if (mine.IsRepaired)
                        {
                            m_minesContent.Add(BuildMineRow(mine, mines++));
                        }
                    }
                    else if (entity is WorldMapCargoShipWreck wreck && (!wreck.IsRepaired || wreck.IsUnderConstruction))
                    {
                        m_repairsContent.Add(BuildRepairRow(wreck, repairs++));
                    }
                }

                AddEmpty(m_repairsContent, repairs == 0, "All discovered entities are repaired.");
                AddEmpty(m_minesContent, mines == 0, "No repaired mines or rigs are available.");
                AddEmpty(m_settlementsContent, settlements == 0, "No settlements discovered.");
                m_status.Value(
                    (m_activeTab == 0 ? repairs + " entities need repair" :
                        m_activeTab == 1 ? mines + " mines & rigs available" :
                        m_activeTab == 2 ? settlements + " settlements discovered" : "Ship preload status").AsLoc());
                RefreshPreload();
            }
            catch
            {
                m_status.Value("World operations are unavailable in this scene.".AsLoc());
            }
        }

        private static void AddEmpty(Column content, bool empty, string text)
        {
            if (empty)
            {
                content.Add(new Label(text.AsLoc()));
            }
        }

        private Row BuildRepairRow(WorldMapRepairableEntity entity, int index)
        {
            Row row = MakeRow(index);
            row.Add(new Label(entity.DefaultTitle).Width(new Px(260f)));
            row.Add(new Label(entity.IsUnderConstruction ? "Repairing...".AsLoc() : "Not repaired".AsLoc()).Width(new Px(150f)));
            row.Add(new Label(FormatCost(entity.CostToRepair).AsLoc()).Width(new Px(250f)));
            var action = new ButtonText(
                Button.General,
                (entity.IsUnderConstruction ? "Cancel" : "Repair").AsLoc(),
                () =>
                {
                    if (entity.IsUnderConstruction)
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityCancelRepairCmd(entity.Id));
                    }
                    else
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityStartRepairCmd(entity.Id));
                    }
                    RefreshAll();
                });
            action.Width(new Px(105f));
            row.Add(action);
            return row;
        }

        private Row BuildMineRow(WorldMapMine mine, int index)
        {
            Row row = MakeRow(index);
            row.Add(new Label(mine.DefaultTitle).Width(new Px(260f)));
            row.Add(
                new Label(
                    mine.IsUnderConstruction ? "Upgrading...".AsLoc() :
                    !mine.UpgradeExists ? "Max level".AsLoc() : ("Level " + mine.Level).AsLoc()).Width(new Px(150f)));
            row.Add(new Label(mine.UpgradeExists ? FormatCost(mine.PriceToUpgrade).AsLoc() : "--".AsLoc()).Width(new Px(250f)));
            var action = new ButtonText(
                Button.General,
                (mine.IsUnderConstruction ? "Cancel" :
                    !mine.UpgradeExists ? "Max level" : "Upgrade").AsLoc(),
                () =>
                {
                    if (mine.IsUnderConstruction)
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityCancelRepairCmd(mine.Id));
                    }
                    else if (mine.UpgradeExists)
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityUpgradeCmd(mine.Id));
                    }
                    RefreshAll();
                });
            action.Width(new Px(105f));
            row.Add(action);
            return row;
        }

        private Row BuildSettlementRow(WorldMapVillage village, int index)
        {
            Row row = MakeRow(index);
            row.Add(new Label(village.DefaultTitle).Width(new Px(260f)));
            row.Add(
                new Label(
                    !village.IsRepaired ? "Not repaired".AsLoc() :
                    village.IsUnderConstruction ? "Upgrading...".AsLoc() : ("Reputation " + village.Reputation).AsLoc()).Width(new Px(150f)));
            row.Add(new Label(village.IsRepaired && village.UpgradeExists ? FormatCost(village.PriceToUpgrade).AsLoc() : "--".AsLoc()).Width(new Px(250f)));
            var action = new ButtonText(
                Button.General,
                (!village.IsRepaired ? "Repair" :
                    village.IsUnderConstruction ? "Cancel" :
                    !village.UpgradeExists ? "Max level" : "Upgrade").AsLoc(),
                () =>
                {
                    if (!village.IsRepaired)
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityStartRepairCmd(village.Id));
                    }
                    else if (village.IsUnderConstruction)
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityCancelRepairCmd(village.Id));
                    }
                    else if (village.UpgradeExists)
                    {
                        m_inputScheduler.ScheduleInputCmd(new WorldMapEntityUpgradeCmd(village.Id));
                    }
                    RefreshAll();
                });
            action.Width(new Px(105f));
            row.Add(action);
            return row;
        }

        private static Row MakeRow(int index)
        {
            Row row = new Row(4.pt()).AlignItemsCenter();
            row.Height(new Px(34f));
            row.RootElement.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            row.RootElement.style.backgroundColor = new StyleColor(index % 2 == 0 ? EvenRow : OddRow);
            row.RootElement.style.paddingLeft = new StyleLength(6f);
            return row;
        }

        private void BuildPreloadPanel()
        {
            m_preloadContent.Add(new Label("Queue cargo using the normal shipyard vehicle buffers. Manual ship orders retain precedence.".AsLoc()));
            m_productPicker = new SingleProductPickerUi(
                GetAvailableProducts,
                product => m_selectedProduct = product,
                () => m_selectedProduct is null ? Option<ProductProto>.None : Option.Some(m_selectedProduct),
                () => m_selectedProduct = null,
                "Pick a product".AsLoc(),
                compact: false,
                primaryButtonIfNoProtoSet: true);
            m_quantity = new TextField().Placeholder("Amount".AsLoc()).Text("100").MaxWidth(new Px(120f));
            var queue = new ButtonText(Button.General, "Queue delivery".AsLoc(), QueuePreload);
            queue.Width(new Px(130f));
            Row controls = new Row(6.pt()).AlignItemsCenter();
            controls.Add(m_productPicker);
            controls.Add(m_quantity);
            controls.Add(queue);
            m_preloadContent.Add(controls);
            m_preloadContent.Add(m_preloadFeedback);
            m_preloadContent.Add(new Label("Trucks bringing in:".AsLoc()));
            m_preloadContent.Add(m_preloadPending);
            m_preloadContent.Add(new Label("In ship cargo:".AsLoc()));
            m_preloadContent.Add(m_preloadCargo);
        }

        private void QueuePreload()
        {
            if (!TajsTweaksRuntimeState.ShipPreload)
            {
                m_preloadFeedback.Value("Enable Ship cargo preload in TajsTweaks settings first.".AsLoc());
                return;
            }

            Shipyard? shipyard = TweaksShipPreloadFeature.GetShipyards().FirstOrDefault();
            if (shipyard is null)
            {
                m_preloadFeedback.Value("No shipyard has been created in this scene.".AsLoc());
                return;
            }
            if (m_selectedProduct is null || !int.TryParse(m_quantity?.GetText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) ||
                quantity <= 0)
            {
                m_preloadFeedback.Value("Choose a product and an amount above zero.".AsLoc());
                return;
            }

            m_preloadFeedback.Value(
                (TweaksShipPreloadFeature.RequestDelivery(shipyard, m_selectedProduct, quantity)
                    ? "Delivery queued through the normal shipyard buffer flow."
                    : "The delivery could not be queued in this scene.").AsLoc());
            RefreshPreload();
        }

        private void RefreshPreload()
        {
            m_preloadPending.Clear();
            m_preloadCargo.Clear();
            IReadOnlyList<Shipyard> shipyards = TweaksShipPreloadFeature.GetShipyards();
            if (shipyards.Count == 0)
            {
                m_preloadPending.Add(new Label("No shipyard found yet.".AsLoc()));
                m_preloadCargo.Add(new Label("No shipyard found yet.".AsLoc()));
                return;
            }

            int pending = 0;
            foreach (Shipyard shipyard in shipyards)
            {
                m_preloadPending.Add(new Label(("Shipyard " + shipyard.Id.Value).AsLoc()).FontBold());
                foreach (TweaksShipPreloadFeature.PendingEntry item in TweaksShipPreloadFeature.ReadPending(shipyard))
                {
                    if (item.Delivered >= item.Target)
                    {
                        continue;
                    }
                    pending++;
                    Row row = new Row(4.pt()).AlignItemsCenter();
                    row.Add(new Label((item.Product.Strings.Name + "  " + item.Delivered + " / " + item.Target).AsLoc()));
                    var cancel = new ButtonText(
                        Button.General,
                        "Cancel".AsLoc(),
                        () =>
                        {
                            TweaksShipPreloadFeature.CancelOrder(shipyard, item.Product);
                            RefreshPreload();
                        });
                    cancel.Width(new Px(90f));
                    row.Add(cancel);
                    m_preloadPending.Add(row);
                }
            }
            if (pending == 0)
            {
                m_preloadPending.Add(new Label("No pending preload deliveries.".AsLoc()));
            }

            int cargoCount = 0;
            foreach (Shipyard shipyard in shipyards)
            {
                foreach (KeyValuePair<ProductProto, int> item in TweaksShipPreloadFeature.ReadCargo(shipyard))
                {
                    cargoCount++;
                    Row row = new Row(4.pt()).AlignItemsCenter();
                    row.Add(new Label(("Shipyard " + shipyard.Id.Value + ": " + item.Key.Strings.Name + "  " + item.Value).AsLoc()));
                    var release = new ButtonText(
                        Button.General,
                        "Release".AsLoc(),
                        () =>
                        {
                            TweaksShipPreloadFeature.Release(shipyard, item.Key);
                            RefreshPreload();
                        });
                    release.Width(new Px(90f));
                    row.Add(release);
                    m_preloadCargo.Add(row);
                }
            }
            if (cargoCount == 0)
            {
                m_preloadCargo.Add(new Label("No reserved cargo yet.".AsLoc()));
            }
        }

        private IEnumerable<ProductProto> GetAvailableProducts()
        {
            return m_productsManager.ProductStats
                .Where(stat => stat?.Product is not null && stat.Product.IsStorable)
                .Select(stat => stat.Product)
                .Distinct()
                .ToArray();
        }

        private static string FormatCost(AssetValue cost)
        {
            if (cost.IsEmpty)
            {
                return "Free";
            }

            var products = new List<string>();
            foreach (ProductQuantity product in cost.Products)
            {
                products.Add(product.Quantity + " " + product.Product.Strings.Name);
            }
            return string.Join(", ", products);
        }
    }
}
