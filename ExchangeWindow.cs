using System;
using System.Reflection;
using System.Globalization;
using Mono.Data.Sqlite;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using Gtk;

namespace NebliDex_Linux
{
    public partial class ExchangeWindow : Gtk.Window
    {
		//UI information
		Gdk.Color gdk_white = new Gdk.Color(255, 255, 255);
		Gdk.Color blackc = new Gdk.Color(0, 0, 0);
		Gdk.Color redc = new Gdk.Color(255, 0, 0);
		Gdk.Color candle_greenc = new Gdk.Color(175, 255, 49);
		Gdk.Color candle_redc = new Gdk.Color(234, 0, 112);
		Gdk.Color candle_darkgreenc = new Gdk.Color(99, 171, 29);
		Gdk.Color dark_ui_foreground = new Gdk.Color(153, 153, 153);
		Gdk.Color dark_ui_panel = new Gdk.Color(9, 11, 13);
		string default_ui_theme = Gtk.Settings.Default.ThemeName; //The default location for the themes
		string default_gtk_dir;
		StatusIcon trayicon;
		private int current_ui_look = 0;

		public int window_width = 0;
		public int window_height = 0;
		public int sellingview_height = 0;
		public int last_candle_time = 0; //The utctime of the last candle
        public int chart_timeline = 0; //24 hours, 1 = 7 days
        public double chart_low = 0, chart_high = 0;

		public NodeView Open_Order_List_Public; //Public interface to NodeView
		public NodeView Trade_History_List_Public;
		public NodeView Recent_Trade_List_Public;
		public NodeView CN_Tx_List_Public;
		public NodeView Wallet_View_Public;

        public ExchangeWindow() :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
         
		    Gdk.Color backc = new Gdk.Color(191, 191, 191);
			default_gtk_dir = Environment.GetEnvironmentVariable("GTK_DATA_PREFIX");
			if(default_gtk_dir == null){
				default_gtk_dir = "/usr/"; //Default location of themes are /usr/share/themes
			}

			this.background.ModifyBg(Gtk.StateType.Normal, backc);
			SetupButtons();

			if (App.my_wallet_pass.Length > 0)
            {
				EncryptWalletAction.Label = "Decrypt Wallet";
            }
         
			this.ResizeChecked += ModifyWindowProp;
			this.DeleteEvent += Window_Close;

			//Load the UI look
			current_ui_look = App.default_ui_look;
			if(current_ui_look == 1){
				LightTheme_Action.Active = true; //Event trigger will run and update
				Change_UI_Look(current_ui_look);
			}else if(current_ui_look == 2){
				DarkTheme_Action.Active = true; //Event trigger will run and update
				Change_UI_Look(current_ui_look);
			}
         
			App.main_window_loaded = true;
            
		}

		public void LoadUI()
        {
            //This function will load the UI based on the data present
            CreateMarketsUI();

            //First Load Wallets
            for (int i = 0; i < App.WalletList.Count;i++){
                Wallet_View.NodeStore.AddNode(App.WalletList[i]);
            }

            //Then Trade history
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Select all the rows from tradehistory
            string myquery = "Select utctime, market, type, price, amount, pending, txhash From MYTRADEHISTORY Order By utctime DESC";
            statement = new SqliteCommand(myquery, mycon);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            while (statement_reader.Read())
            {
                string format_date = "";
                string format_type;
                string format_market = "";
                string txhash = statement_reader["txhash"].ToString();
                int utctime = Convert.ToInt32(statement_reader["utctime"]);
                if (Convert.ToInt32(statement_reader["pending"]) == 0)
                {
                    format_date = App.UTC2DateTime(utctime).ToString("yyyy-MM-dd");
                }
                else if (Convert.ToInt32(statement_reader["pending"]) == 1)
                {
                    format_date = "PENDING";
                }
                else
                {
                    format_date = "CANCELLED";
                }
                if (Convert.ToInt32(statement_reader["type"]) == 0)
                {
                    format_type = "BUY";
                }
                else
                {
                    format_type = "SELL";
                }
                int market = Convert.ToInt32(statement_reader["market"]);
				decimal price = Decimal.Parse(statement_reader["price"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                decimal amount = Decimal.Parse(statement_reader["amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                format_market = App.MarketList[market].format_market;
                Trade_History_List.NodeStore.AddNode(new App.MyTrade { Date = format_date, Pair = format_market, Type = format_type, Price = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price), Amount = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", amount), TxID = txhash });
            }
            statement_reader.Close();
            statement.Dispose();

            //Load from the CN fees table to the chart
            //Select all the rows from tradehistory
            myquery = "Select utctime, market, fee From CNFEES Order By utctime DESC";
            statement = new SqliteCommand(myquery, mycon);
            statement_reader = statement.ExecuteReader();
            while (statement_reader.Read())
            {
                string format_date = "";
                string format_market = "";
                int utctime = Convert.ToInt32(statement_reader["utctime"]);
                format_date = App.UTC2DateTime(utctime).ToString("yyyy-MM-dd");
                int market = Convert.ToInt32(statement_reader["market"]);
				decimal fee = Decimal.Parse(statement_reader["fee"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                format_market = App.MarketList[market].format_market;
                CN_Tx_List.NodeStore.AddNode(new App.MyCNFee{ Date = format_date, Pair = format_market, Fee = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", fee) });
            }
            statement_reader.Close();
            statement.Dispose();

            //Now load the Candle information for the visible chart (NDEX/NEBL is default market & 24 HR is default timeline) from Sqlite DB (added during sync period)
            //The server will send how many seconds left on the most recent candle (for both times), before moving forward
            int backtime = App.UTCTime() - 60 * 60 * 25;
            myquery = "Select highprice, lowprice, open, close From CANDLESTICKS24H Where market = @mark And utctime > @time Order By utctime ASC"; //Show results from oldest to most recent
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.Parameters.AddWithValue("@mark", App.exchange_market);
            statement_reader = statement.ExecuteReader();
            while (statement_reader.Read())
            {
                //Go Candle by Candle to get results
                App.Candle can = new App.Candle();
                //Must use cultureinfo as some countries see . as ,
                can.open = Convert.ToDouble(statement_reader["open"], CultureInfo.InvariantCulture);
                can.close = Convert.ToDouble(statement_reader["close"], CultureInfo.InvariantCulture);
                can.low = Convert.ToDouble(statement_reader["lowprice"], CultureInfo.InvariantCulture);
                can.high = Convert.ToDouble(statement_reader["highprice"], CultureInfo.InvariantCulture);
                PlaceCandleInChart(can);
            }
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();

            //Add a recent candle based on the last trade
            lock (App.ChartLastPrice)
            {
                AddCurrentCandle();
            }

            lock(App.RecentTradeList[App.exchange_market])
            {
				//Most recent trade is on top (which is last trade)
				for (int i = 0; i < App.RecentTradeList[App.exchange_market].Count;i++){
					Recent_Trade_List.NodeStore.AddNode(App.RecentTradeList[App.exchange_market][i]);
                }
            }

            //This List will be pre-sorted for highest price to lowest
            //There is no autosort for NodeView

            //Populate the sell list first
            lock (App.OpenOrderList[App.exchange_market])
            {
                for (int i = 0; i < App.OpenOrderList[App.exchange_market].Count; i++)
                {
					if (App.OpenOrderList[App.exchange_market][i].type == 1 && App.OpenOrderList[App.exchange_market][i].order_stage == 0)
                    {
						//Sell Orders
						AddSortedOrderToView(Selling_View, App.OpenOrderList[App.exchange_market][i]);
                    }
                }

                //And buy list then
                for (int i = 0; i < App.OpenOrderList[App.exchange_market].Count; i++)
                {
					if (App.OpenOrderList[App.exchange_market][i].type == 0 && App.OpenOrderList[App.exchange_market][i].order_stage == 0)
                    {
                        //Buy Orders
						AddSortedOrderToView(Buying_View, App.OpenOrderList[App.exchange_market][i]);
                    }
                    
                }
            }
            
			//Scroll Automatically to Bottom for Sell List
			int row = NodeViewRowCount(Selling_View);
            if (row > 0)
            {
				//Pad the rows so that the active orders are aligned to bottom
				AddPaddingForOrderView(Selling_View, row);
                Selling_View.ScrollToCell(new TreePath(new int[] {row - 1 }), null, false, 0, 0);
            }

            //Show the initial fees as well
            UpdateBlockrates();

        }
        
        public void AddCurrentCandle()
        {
            if (App.ChartLastPrice[chart_timeline].Count == 0) { return; }
            //This will add a new candle based on current last chart prices
            //Then Load the current candle into the chart. This candle is not stored in database and based soley and chartlastprice
            double open = -1, close = -1, high = -1, low = -1;
            for (int pos = 0; pos < App.ChartLastPrice[chart_timeline].Count; pos++)
            {
				if (App.ChartLastPrice[chart_timeline][pos].market == App.exchange_market)
                {
                    double price = Convert.ToDouble(App.ChartLastPrice[chart_timeline][pos].price);
                    if (open < 0) { open = price; }
                    if (price > high)
                    {
                        high = price;
                    }
                    if (low < 0 || price < low)
                    {
                        low = price;
                    }
                    close = price; //The last price will be the close
                }
            }
            if (open > 0)
            {
                //May not have any candles for this market
                App.Candle new_can = new App.Candle();
                new_can.open = open;
                new_can.close = close;
                new_can.high = high;
                new_can.low = low;
                PlaceCandleInChart(new_can);
			}else{
				Chart_Canvas.QueueDraw(); //Draw the canvas with no candles
			}
        }
        
        public void PlaceCandleInChart(App.Candle can)
        {
            //First adjust the candle high and low if the open and close are the same
			if (Math.Round(can.high,8) == Math.Round(can.low,8))
            {
                can.high = can.high + App.double_epsilon; //Allow us to create a range
                can.low = can.low - App.double_epsilon;
                if (can.low < 0) { can.low = 0; }
            }

            //And it will add it to the list
            if (App.VisibleCandles.Count >= 100)
            {
                App.VisibleCandles.RemoveAt(0); //Remove the first / oldest candle
            }
            App.VisibleCandles.Add(can);
            AdjustCandlePositions();
            last_candle_time = App.UTCTime();
        }

		public void UpdateLastCandle(double val)
        {
            if (App.VisibleCandles.Count == 0)
            {
                //Make a new candle, how history exists
                App.Candle can = new App.Candle();
                can.open = val;
                can.close = val;
                can.high = val;
                can.low = val; ;
                PlaceCandleInChart(can);
            }
            else
            {
                //This will update the value for the last candle
                App.Candle can = App.VisibleCandles[App.VisibleCandles.Count - 1]; //Get last candle
                if (val > can.high)
                {
                    can.high = val;
                }
                if (val < can.low)
                {
                    can.low = val;
                }

                can.close = val;

                //Look at the last chartlastprice to find the close price for the candle
                int timeline = chart_timeline;
                for (int i = App.ChartLastPrice[timeline].Count - 1; i >= 0; i--)
                {
                    if (App.ChartLastPrice[timeline][i].market == App.exchange_market)
                    {
						can.close = Convert.ToDouble(App.ChartLastPrice[timeline][i].price);
                        break;
                    }
                }
                
            }
            AdjustCandlePositions();
        }

		public void AdjustCandlePositions()
		{
			//This will only update the labels
			if (App.VisibleCandles.Count == 0) { return; }
			double last_open = App.VisibleCandles[0].open;
			if (last_open <= 0) { return; } //We do not want divide by zero error
			//Change the Market Percent
            
			double change = Math.Round((App.VisibleCandles[App.VisibleCandles.Count - 1].close - App.VisibleCandles[0].open) / App.VisibleCandles[0].open * 100, 2);
			if (change == 0)
            {
				Market_Percent.Markup = "<span font='13'><b>00.00%</b></span>";
				Market_Percent.ModifyFg(Gtk.StateType.Normal, gdk_white);
				if(current_ui_look == 1){
					Market_Percent.ModifyFg(Gtk.StateType.Normal, blackc);
				}else if (current_ui_look == 2)
                {
                    Market_Percent.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
                }
            }
            else if (change > 0)
            {
				//Green
				Market_Percent.ModifyFg(Gtk.StateType.Normal, candle_greenc);
				if(current_ui_look == 1){
					//White
					Market_Percent.ModifyFg(Gtk.StateType.Normal, candle_darkgreenc);
				}
                if (change > 10000)
                {
					Market_Percent.Markup = "<span font='13'><b>> +10000%</b></span>";
                }
                else
                {
					Market_Percent.Markup = "<span font='13'><b>+" + change.ToString(CultureInfo.InvariantCulture) + "%</b></span>";
                }
            }
            else if (change < 0)
            {
				Market_Percent.ModifyFg(Gtk.StateType.Normal, candle_redc);
                if (change < -10000)
                {
					Market_Percent.Markup = "<span font='13'><b>> -10000%</b></span>";
                }
                else
                {
					Market_Percent.Markup = "<span font='13'><b>" + change.ToString(CultureInfo.InvariantCulture) + "%</b></span>";
                }
            }
			Chart_Last_Price.Markup = "<span font='10'><b>Last Price: " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", App.VisibleCandles[App.VisibleCandles.Count - 1].close)+"</b></span>";
			Chart_Canvas.QueueDraw();
		}

		public void DrawCandlePositions(Cairo.Context buf, int can_width, int can_height)
        {
            //This function will draw the canvas and graphs to make candles appear well
            if (Double.IsInfinity(can_height)) { return; }
            if (Double.IsNaN(can_height)) { return; }
            if (can_height <= 0) { return; }
            if (App.VisibleCandles.Count == 0) { return; }
         
            double lowest = -1;
            double highest = -1;
            for (int i = 0; i < App.VisibleCandles.Count; i++)
            {
                //Go through each candle to find maximum height and maximum low
                if (App.VisibleCandles[i].low < lowest || lowest < 0)
                {
                    lowest = App.VisibleCandles[i].low;
                }
                if (App.VisibleCandles[i].high > highest || highest < 0)
                {
                    highest = App.VisibleCandles[i].high;
                }
            }

            double middle = (highest - lowest) / 2.0; //Should be the middle of the chart
            if (middle <= 0) { return; } //Shouldn't happen unless flat market

			//Make it so that the candles don't hit buttons
			lowest = lowest - (highest - lowest) * 0.05;
			highest = highest + (highest - lowest) * 0.05;

            //Calculate Scales
            double ChartScale = can_height / (highest - lowest);
            double width = can_width / 100.0;

            //Position Candles based on scale and width
            //Total of 100 candles visible so each candle needs to be 1/100 of chart
            double xpos = 0;
            double ypos = 0;
            double candles_width = App.VisibleCandles.Count * width; //The width of the entire set of candles
            double height = 0;
            for (int i = 0; i < App.VisibleCandles.Count; i++)
            {
                xpos = (can_width - candles_width); //Start position
                xpos = xpos + i * width; //Current Position
            
                //Calculate height now
                if (App.VisibleCandles[i].open > App.VisibleCandles[i].close)
                {
                    //Red Candle
                    height = (App.VisibleCandles[i].open - App.VisibleCandles[i].close) * ChartScale; //Calculate Height
                    ypos = can_height - (App.VisibleCandles[i].open - lowest) * ChartScale; //Top Left Corner is 0,0 so position for top of rect
                }
                else
                {
                    //Green candle
                    height = (App.VisibleCandles[i].close - App.VisibleCandles[i].open) * ChartScale; //Calculate Height
                    ypos = can_height - (App.VisibleCandles[i].close - lowest) * ChartScale; //Top Left Corner is 0,0 so position for top of rect
                }

                if (height < 1) { height = 1; } //Show something

				//Calculate Outliers and draw them first
				buf.SetSourceRGB(gdk_white.Red, gdk_white.Green, gdk_white.Blue);
				if(current_ui_look == 1){
					//White background, gray outliers
					buf.SetSourceRGB(128.0 / 255.0, 128.0 / 255.0, 128.0 / 255.0);
				}
                if (App.VisibleCandles[i].high - App.VisibleCandles[i].low >= App.double_epsilon * 2.1)
                {
                    double line_height = (App.VisibleCandles[i].high - App.VisibleCandles[i].low) * ChartScale;
                    if (line_height < 1) { line_height = 1; } //Show something
                    double line_ypos = can_height - (App.VisibleCandles[i].high - lowest) * ChartScale; //Position for top of line
					double y1 = line_ypos;
					double y2 = line_ypos+line_height;
					double x1 = xpos + (width / 2.0);
					double x2 = xpos + (width / 2.0);
					buf.LineWidth = 1;
					buf.MoveTo(x1,y1);
					buf.LineTo(x2,y2);
					buf.Stroke();
                }
				//Don't draw a line if the difference is too small

				//OK now draw the rectangle
				if (App.VisibleCandles[i].open > App.VisibleCandles[i].close)
				{
					//Red candle
					double red = Convert.ToDouble(234) / 255.0;
					double green = Convert.ToDouble(0) / 255.0;
					double blue = Convert.ToDouble(112) / 255.0;
					buf.SetSourceRGB(red,green,blue);
				}else{
					//Green candle
					double red = Convert.ToDouble(175) / 255.0;
                    double green = Convert.ToDouble(255) / 255.0;
                    double blue = Convert.ToDouble(49) / 255.0;
					if(current_ui_look == 1){
						//White
						red = Convert.ToDouble(99) / 255.0;
                        green = Convert.ToDouble(171) / 255.0;
                        blue = Convert.ToDouble(29) / 255.0;
					}
					buf.SetSourceRGB(red, green, blue);
				}
				buf.Rectangle(xpos, ypos, width, height);
				buf.Fill();

            }

            chart_low = lowest;
            chart_high = highest;
            
        }

		private void Chart_MouseMove(object sender, MotionNotifyEventArgs e)
        {
			//This function is executed when mouse moved over chart
			last_candle_time = App.UTCTime();
            if (last_candle_time <= 0) { return; }
            if (Chart_Canvas.Allocation.Width <= 0) { return; }

			double x = e.Event.X;
			double y = e.Event.Y;

            int old_candle_time = 0;
            if (chart_timeline == 0)
            {
                old_candle_time = last_candle_time - 60 * 60 * 24;
            }
            else
            {
                old_candle_time = last_candle_time - 60 * 60 * 24 * 7;
            }
            double gridwidth = Chart_Canvas.Allocation.Width / 100.0;
            int grid = (int)Math.Floor(x / gridwidth);

            gridwidth = (last_candle_time - old_candle_time) / 100.0;
            int gridtime = old_candle_time + (int)Math.Round(grid * gridwidth);

			if (Math.Abs(chart_high - chart_low) < App.double_epsilon/2.0) { return; }
            double ratio = (chart_high - chart_low) / Chart_Canvas.Allocation.Height;
            double price = Math.Round(chart_low + (Chart_Canvas.Allocation.Height - y) * ratio, 8);
			Chart_Mouse_Price.Visible = true;
            if (price < 0) { price = 0; Chart_Mouse_Price.Visible = false; }
            
			Chart_Mouse_Price.Markup = "<span font='10'>"+App.UTC2DateTime(gridtime).ToString("yyyy-MM-dd HH:mm") + " | " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", price)+"</span>";
        }

		private void Check_Row_Selection(object sender, EventArgs e)
		{
			//This will be fired when the Selling View rows are changed
			NodeView view = (NodeView)sender;
            App.OpenOrder ord = (App.OpenOrder)view.NodeSelection.SelectedNode; //This is order
            if (ord == null) { return; }
			if(ord.filled_node == false){
				//Deselect the row that is marked as not filled
				view.Selection.UnselectAll();
			}
		}

		private void Select_Order(object sender, EventArgs e)
        {
            if (App.critical_node == true)
            {
                App.MessageBox(this,"Notice","Cannot Match An Order in Critical Node Mode","OK");
                return;
            }

            if (Convert.ToString(Market_Percent.Text) == "LOADING...") { return; }
            
			NodeView view = (NodeView)sender;
			App.OpenOrder ord = (App.OpenOrder)view.NodeSelection.SelectedNode; //This is order
			if (ord == null) { return; }

            //Verify that order is not our own order
            bool notmine = true;
            lock (App.MyOpenOrderList)
            {
                for (int i = 0; i < App.MyOpenOrderList.Count; i++)
                {
                    if (App.MyOpenOrderList[i].order_nonce == ord.order_nonce)
                    {
                        notmine = false; break;
                    }
                }
            }

            if (notmine == true)
            {
                MatchOrderWindow m_dialog = new MatchOrderWindow(ord);
				m_dialog.Parent = this;
				m_dialog.Modal = true;
				m_dialog.Show();
            }
            else
            {
                App.MessageBox(this,"Notice","Cannot match with your own order!","OK");
            }

        }

		public int Selling_View_Timer = 0;
        public int Buying_View_Timer = 0;

        private void Reset_AutoScroll(object sender, EventArgs e)
        {
            //Everytime the list item is touched, it doesn't update on new order for 5 seconds
            var my_list = sender as NodeView;
            if (my_list == Selling_View)
            {
                Selling_View_Timer = App.UTCTime();
            }
            else if (my_list == Buying_View)
            {
                Buying_View_Timer = App.UTCTime();
            }
        }

		public void RefreshUI()
        {
            //This will reload the visuals on all the lists and charts for a new market
            //0 - NEBL/BTC
            //1 - NEBL/LTC
            //2 - NDEX/NEBL

            //Update the buttons
			Gtk.Label buy_button_label = (Gtk.Label)Buy_Button.Children[0];
			buy_button_label.Markup = "<span font='12.5'>Buy " + App.MarketList[App.exchange_market].trade_symbol+"</span>";
            Gtk.Label sell_button_label = (Gtk.Label)Sell_Button.Children[0];
			sell_button_label.Markup = "<span font='12.5'>Sell " + App.MarketList[App.exchange_market].trade_symbol+"</span>";

			//Clear the Order List for the market and reload for the new market
			Selling_View.NodeStore.Clear();
			Buying_View.NodeStore.Clear();

			lock (App.OpenOrderList[App.exchange_market])
            {
                for (int i = 0; i < App.OpenOrderList[App.exchange_market].Count; i++)
                {
					if (App.OpenOrderList[App.exchange_market][i].type == 1 && App.OpenOrderList[App.exchange_market][i].order_stage == 0)
                    {
                        //Sell Orders
                        AddSortedOrderToView(Selling_View, App.OpenOrderList[App.exchange_market][i]);
                    }
                }

                //And buy list then
                for (int i = 0; i < App.OpenOrderList[App.exchange_market].Count; i++)
                {
					if (App.OpenOrderList[App.exchange_market][i].type == 0 && App.OpenOrderList[App.exchange_market][i].order_stage == 0)
                    {
                        //Buy Orders
                        AddSortedOrderToView(Buying_View, App.OpenOrderList[App.exchange_market][i]);
                    }
                }
            }

            //Re-position Lists
			int row = NodeViewRowCount(Selling_View);
            if (row > 0)
            {
				AddPaddingForOrderView(Selling_View, row);
                Selling_View.ScrollToCell(new TreePath(new int[] { row - 1 }), null, false, 0, 0);
            }
			row = NodeViewRowCount(Buying_View);
            if (row > 0)
            {
                Buying_View.ScrollToCell(new TreePath(new int[] { 0 }), null, false, 0, 0);
            }

			//Change the recent trade list
			Recent_Trade_List.NodeStore.Clear();
			lock (App.RecentTradeList[App.exchange_market])
            {
                //Most recent trade is on top (which is last trade)
                for (int i = 0; i < App.RecentTradeList[App.exchange_market].Count; i++)
                {
                    Recent_Trade_List.NodeStore.AddNode(App.RecentTradeList[App.exchange_market][i]);
                }
            }

            //Update the Candles as well
            lock (App.ChartLastPrice)
            {
                UpdateCandles();
            }

            //Update block rates
            UpdateBlockrates();

        }

		public void UpdateBlockrates()
        {
            //Make sure all the Dex connections exists
            bool not_connected = false;
            //contype 1 now represents all electrum connections but different cointypes
            lock (App.DexConnectionList)
            {
                bool connnection_exist;
                for (int cit = 1; cit < App.total_cointypes; cit++)
                {
                    //Go through all the blockchain types and make sure an electrum connection exists for it, skip Neblio blockchain as it doesn't use electrum
                    if (cit == 6) { continue; } //Etheruem doesn't use dexconnection
                    connnection_exist = false;
                    for (int i = 0; i < App.DexConnectionList.Count; i++)
                    {
                        if (App.DexConnectionList[i].open == true && App.DexConnectionList[i].contype == 1 && App.DexConnectionList[i].blockchain_type == cit)
                        {
                            connnection_exist = true;
                            break;
                        }
                    }
                    if (connnection_exist == false)
                    {
                        not_connected = true;
                        break;
                    }
                }
                //Now detect if client is connected to a CN node
                if (App.critical_node == false)
                {
                    connnection_exist = false;
                    for (int i = 0; i < App.DexConnectionList.Count; i++)
                    {
                        if (App.DexConnectionList[i].open == true && App.DexConnectionList[i].contype == 3)
                        {
                            connnection_exist = true;
                            break;
                        }
                    }
                    if (connnection_exist == false)
                    {
                        not_connected = true;
                    }
                }
            }


			if (not_connected == false && App.ntp1downcounter < 2)
            {
                //Update the block rate status bar based on the market
				Fee_Status.Markup = "<span font='8'><b>Current Blockchain Fees:</b></span>";
                if(App.using_blockhelper == true){
					Fee_Status.Markup = "<span font='8'><b>BlockHelper Active | Current Blockchain Fees:</b></span>";
                }
				if (current_ui_look != 2)
				{
					Fee_Status.ModifyFg(Gtk.StateType.Normal, blackc);
				}else{
					Fee_Status.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
				}
				CN_Fee.Markup = "<span font='8'>CN Fee: " + App.ndex_fee + " | Taker Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.##}", Math.Round(App.taker_fee * 100, 2)) + "%</span>";

				int trade_wallet_blockchaintype = App.GetWalletBlockchainType(App.MarketList[App.exchange_market].trade_wallet);
                int base_wallet_blockchaintype = App.GetWalletBlockchainType(App.MarketList[App.exchange_market].base_wallet);

                //Update Status Bar Fees               
				if (trade_wallet_blockchaintype == 0)
                {
					NEBL_Fee.Markup = "<span font='8'> | NEBL Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", Math.Round(App.blockchain_fee[trade_wallet_blockchaintype], 8)) + "/kb</span>";
                }
                else if (trade_wallet_blockchaintype == 6)
                {
					NEBL_Fee.Markup = "<span font='8'> | ETH Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.##}", Math.Round(App.blockchain_fee[trade_wallet_blockchaintype], 2)) + " Gwei</span>";
                }
                else
                {
					NEBL_Fee.Markup = "<span font='8'> | " + App.MarketList[App.exchange_market].trade_symbol + " Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", Math.Round(App.blockchain_fee[trade_wallet_blockchaintype], 8)) + "/kb</span>";
                }

                if (trade_wallet_blockchaintype != base_wallet_blockchaintype)
                {
                    //Show both the trade and base fees

					if (base_wallet_blockchaintype == 0)
                    {
						//NEBL Base
						Base_Pair_Fee.Markup = "<span font='8'> | NEBL Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", Math.Round(App.blockchain_fee[base_wallet_blockchaintype], 8)) + "/kb</span>";
                    }
                    else if (base_wallet_blockchaintype == 6)
                    {
                        //ETH Base
						Base_Pair_Fee.Markup = "<span font='8'> | ETH Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.##}", Math.Round(App.blockchain_fee[base_wallet_blockchaintype], 2)) + " Gwei</span>";
                    }
                    else
                    {
						Base_Pair_Fee.Markup = "<span font='8'> | " + App.MarketList[App.exchange_market].base_symbol + " Fee: " + String.Format(CultureInfo.InvariantCulture, "{0:0.########}", Math.Round(App.blockchain_fee[base_wallet_blockchaintype], 8)) + "/kb</span>";
                    }
                }
                else
                {
                    //Only show the trade fee as they use the same blockchaintype
					Base_Pair_Fee.Markup = "";
                }

                if (App.critical_node == true)
                {
                    int cn_online = App.CN_Nodes_By_IP.Count;
                    string percent = String.Format(CultureInfo.InvariantCulture, "{0:0.###}", Math.Round(App.my_cn_weight * 100, 3));
					CN_Info.Markup = "<span font='8'> | Tx Validating: " + App.cn_num_validating_tx + " (CNs Online: " + cn_online + ", " + percent + "% Chance of Validating)</span>";
                }
                else
                {
                    CN_Info.Text = "";
                }
            }
            else
            {
				Fee_Status.Markup = "<span font='10'><b>Not Fully Connected:</b></span>";
				Fee_Status.ModifyFg(Gtk.StateType.Normal, redc);
            }

        }

		public void AddOrderToView(App.OpenOrder ord)
        {
            //This function adds an order the view
            if (ord.market != App.exchange_market) { return; } //Not on this market, do not add to view
			Application.Invoke(delegate
            {
				//Must be on UI thread
                if (ord.type == 0)
                {
					//Buying view
					AddSortedOrderToView(Buying_View, ord);
                    if (App.UTCTime() - Buying_View_Timer > 5)
                    {
						//Auto scroll
						Buying_View.ScrollToCell(new TreePath(new int[] { 0 }), null, false, 0, 0);
                    }
                }
                else if (ord.type == 1)
                {
					RemovePaddingForOrderView(Selling_View);
					AddSortedOrderToView(Selling_View, ord);
					int row = NodeViewRowCount(Selling_View);
					AddPaddingForOrderView(Selling_View, row);
                    if (App.UTCTime() - Selling_View_Timer > 5)
                    {
                        Selling_View.ScrollToCell(new TreePath(new int[] { row - 1 }), null, false, 0, 0);
                    }
                }
            });
        }

        public void RemoveOrderFromView(App.OpenOrder ord)
        {
            //This function adds an order the view
            if (ord.market != App.exchange_market) { return; } //Not on this market, do not need to remove to view
			Application.Invoke(delegate
            {
				//Must be on UI thread
                if (ord.type == 0)
                {
					//Buying view
					int rows = NodeViewRowCount(Buying_View);
					for (int i = rows - 1; i >= 0; i--){
						TreePath path = new TreePath(new int[] { i }); //Horrible waste of memory but no other option
                        App.OpenOrder row_ord = (App.OpenOrder)Buying_View.NodeStore.GetNode(path);                  
						path.Dispose();
						if (row_ord == null) { break; } //Possible duplicate nodes
						if(row_ord.order_nonce.Equals(ord.order_nonce) == true){
							//Remove this from the nodestore
							Buying_View.NodeStore.RemoveNode(row_ord);
						}
					}
                }
                else if (ord.type == 1)
                {
					//Selling view
					RemovePaddingForOrderView(Selling_View);
                    int rows = NodeViewRowCount(Selling_View);
                    for (int i = rows - 1; i >= 0; i--)
                    {
                        TreePath path = new TreePath(new int[] { i }); //Horrible waste of memory but no other option
                        App.OpenOrder row_ord = (App.OpenOrder)Selling_View.NodeStore.GetNode(path);
                        path.Dispose();
						if (row_ord == null) { break; } //Possible duplicate nodes
                        if (row_ord.order_nonce.Equals(ord.order_nonce) == true)
                        {
                            //Remove this from the nodestore
                            Selling_View.NodeStore.RemoveNode(row_ord);
                        }
                    }
					rows = NodeViewRowCount(Selling_View);
					AddPaddingForOrderView(Selling_View, rows);
                }
            });
        }

		public static void PendOrder(string nonce)
        {
            //This is a CN to CN or to TN function
			App.OpenOrder ord = null;
            for (int market = 0; market < App.total_markets; market++)
            {
                lock (App.OpenOrderList[market])
                {
                    for (int i = 0; i < App.OpenOrderList[market].Count; i++)
                    {
                        if (App.OpenOrderList[market][i].order_nonce.Equals(nonce) == true)
                        {
                            if (App.OpenOrderList[market][i].order_stage > 0) { break; } //Shouldn't happen normally
                            App.OpenOrderList[market][i].pendtime = App.UTCTime(); //This pended order will remove itself in 3 hours if still pending
                            App.OpenOrderList[market][i].order_stage = 1; //Pending
                            ord = App.OpenOrderList[market][i];
                            break;
                        }
                    }
                }
            }

            if (ord == null) { return; }
            if (App.main_window_loaded == false) { return; }
            if (App.exchange_market != ord.market) { return; }

			App.main_window.RemoveOrderFromView(ord); //Will remove any matching orders by nonce
            
        }

        public static bool ShowOrder(string nonce)
        {
            //This is a CN to CN and to TN function
			App.OpenOrder ord = null;
            for (int market = 0; market < App.total_markets; market++)
            {
                lock (App.OpenOrderList[market])
                {
                    for (int i = 0; i < App.OpenOrderList[market].Count; i++)
                    {
                        if (App.OpenOrderList[market][i].order_nonce.Equals(nonce) == true)
                        {
                            if (App.OpenOrderList[market][i].my_order == true)
                            {
                                //This order nonce belongs to me
                                if (App.OpenOrderList[market][i].order_stage > 0) { break; } //Shouldn't happen normally
                            }
                            App.OpenOrderList[market][i].order_stage = 0; //Show order
                            ord = App.OpenOrderList[market][i];
                            break;
                        }
                    }
                }
            }

            if (ord == null) { return false; }
            if (App.main_window_loaded == false) { return false; }
            if (App.exchange_market != ord.market) { return true; } //Still valid, just not showing the market

			Application.Invoke(delegate
            {
                //Must be on UI thread
                //We will re-add the nodes when they are available again
                if (ord.type == 0)
                {
					//Buying view
					App.main_window.AddSortedOrderToView(App.main_window.Buying_View, ord);
                }
                else if (ord.type == 1)
                {
					App.main_window.RemovePaddingForOrderView(App.main_window.Selling_View);
					App.main_window.AddSortedOrderToView(App.main_window.Selling_View, ord);
					int row_num = NodeViewRowCount(App.main_window.Selling_View);
					App.main_window.AddPaddingForOrderView(App.main_window.Selling_View,row_num);
                }
            });
            return true;
        }

        public void CreateMarketsUI()
        {

			//Update combo box to show markets
			List<string> market_string = new List<string>();
			//We will use this to eventually sort the market box
            for (int i = 0; i < App.MarketList.Count; i++)
            {
				if (App.MarketList[i].active == false)
                {
                    continue;
                }              
				string format_market = App.MarketList[i].format_market;
                //We are going to alphabetically sort the marketlist
                bool not_found = true;
                for (int i2 = 0; i2 < market_string.Count; i2++)
                {
                    string item_detail = (string)market_string[i2];
                    int compare = String.Compare(format_market, item_detail, true);
                    if (compare < 0)
                    {
                        not_found = false;
						//Format Market precedes item_detail, add it in front
						market_string.Insert(i2, App.MarketList[i].format_market);
                        break;
                    }
                }
                if (not_found == true)
                {
					market_string.Add(App.MarketList[i].format_market);
                }
            }
			//Now create the sorted markets based on the market_string
			for (int i = 0; i < market_string.Count;i++)
			{
				Market_Box.AppendText(market_string[i]);
				if(App.MarketList[App.exchange_market].format_market == market_string[i]){
					//This is our market
					Market_Box.Active = i;
				}
			}
        }

		public void showTradeMessage(string msg)
        {
            if (msg.Length > 200) { return; } //Really long message, do not show
			if(App.run_headless == false){
				Application.Invoke(delegate
                {
                    App.MessageBox(this, "NebliDex: Trade Notice!", msg, "OK");
                });				
			}else{
				Console.WriteLine(msg);
			}
        }

		public void UpdateCandles()
        {
            //Clear the Visible Candles and reload the charts for the appropriate timescale and market
            App.VisibleCandles.Clear();

			Market_Percent.ModifyFg(Gtk.StateType.Normal, gdk_white);
			if (current_ui_look == 1)
            {
                Market_Percent.ModifyFg(Gtk.StateType.Normal, blackc);
			}else if(current_ui_look == 2){
				Market_Percent.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
			}
			Market_Percent.Markup = "<span font='13'><b>00.00%</b></span>";
			Chart_Last_Price.Markup = "<span font='10'><b>Last Price:</b></span>";

            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            string myquery = "";
            int backtime = 0;

            if (chart_timeline == 0)
            { //24 hr
                backtime = App.UTCTime() - 60 * 60 * 25;
                myquery = "Select highprice, lowprice, open, close From CANDLESTICKS24H Where market = @mark And utctime > @time Order By utctime ASC";
            }
            else if (chart_timeline == 1)
            { //7 day
                backtime = App.UTCTime() - (int)Math.Round(60.0 * 60.0 * 24.0 * 6.25); //Closer to actual time of 100 candles
                myquery = "Select highprice, lowprice, open, close From CANDLESTICKS7D Where market = @mark And utctime > @time Order By utctime ASC";
            }

            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.Parameters.AddWithValue("@mark", App.exchange_market);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            while (statement_reader.Read())
            {
                //Go Candle by Candle to get results
                App.Candle can = new App.Candle();
                //Must use cultureinfo as some countries see . as ,
                can.open = Convert.ToDouble(statement_reader["open"], CultureInfo.InvariantCulture);
                can.close = Convert.ToDouble(statement_reader["close"], CultureInfo.InvariantCulture);
                can.low = Convert.ToDouble(statement_reader["lowprice"], CultureInfo.InvariantCulture);
                can.high = Convert.ToDouble(statement_reader["highprice"], CultureInfo.InvariantCulture);
                PlaceCandleInChart(can);
            }
            statement_reader.Close();
            statement.Dispose();

            mycon.Close();

            AddCurrentCandle();
        }

		public static void UpdateOpenOrderList(int market, string order_nonce)
        {
            //This function will remove 0 sized orders
            //First find the open order from our list
            App.OpenOrder ord = null;
            lock (App.OpenOrderList[market])
            {
                for (int i = App.OpenOrderList[market].Count - 1; i >= 0; i--)
                {
                    if (App.OpenOrderList[market][i].order_nonce == order_nonce && App.OpenOrderList[market][i].is_request == false)
                    {
                        ord = App.OpenOrderList[market][i];
                        if (App.OpenOrderList[market][i].amount <= 0)
                        { //Take off the order if its empty now
                            App.OpenOrderList[market].RemoveAt(i); break;
                        }
                    }
                }
            }

            if (ord == null) { return; }

			if (market == App.exchange_market && App.main_window_loaded == true)
			{
				Application.Invoke(delegate
				{
					if (ord != null)
					{
						if (ord.amount <= 0)
						{
							App.main_window.RemoveOrderFromView(ord);
						}
					}
				});
			}
        }

        public static void AddRecentTradeToView(int market, int type, decimal price, decimal amount, string order_nonce, int time)
        {

            if (amount <= 0) { return; } //Someone is trying to hack the system

            //First check if our open orders matches this recent trade
			App.OpenOrder myord = null;
            lock (App.MyOpenOrderList)
            {
                for (int i = App.MyOpenOrderList.Count - 1; i >= 0; i--)
                {
                    if (App.MyOpenOrderList[i].order_nonce == order_nonce)
                    {
                        if (App.MyOpenOrderList[i].is_request == false)
                        {
                            myord = App.MyOpenOrderList[i];
                            break;
                        }
                        else
                        {
                            //I am requesting this order, should only occur during simultaneous order request
                            //when only one of the orders match. Clear this request and tell user someone else took order
                            App.MyOpenOrderList.RemoveAt(i);
							if (App.main_window_loaded == true)
							{
								App.main_window.showTradeMessage("Trade Failed:\nSomeone else matched this order before you!");
								//Update the view
								App.OpenOrder myord_delegate = App.MyOpenOrderList[i];
								Application.Invoke(delegate
								{
									App.main_window.Open_Orders_List.NodeStore.RemoveNode(myord_delegate);
								});
							}
                        }
                    }
                }
            }

            if (App.critical_node == false)
            {
                if (market != App.exchange_market) { return; } //We don't care about recent trades from other markets if not critical node
            }

            //Also modify the open order that is not our order
            lock (App.OpenOrderList[market])
            {
                for (int i = App.OpenOrderList[market].Count - 1; i >= 0; i--)
                {
                    if (App.OpenOrderList[market][i].order_nonce == order_nonce && App.OpenOrderList[market][i].is_request == false)
                    {
                        if (App.OpenOrderList[market][i] != myord)
                        {
                            App.OpenOrderList[market][i].amount -= amount; //We already subtracted the amount if my order
                            App.OpenOrderList[market][i].amount = Math.Round(App.OpenOrderList[market][i].amount, 8);
                        }
                    }
                }
            }

            //This will also calculate the chartlastprice and modify the candle

            App.RecentTrade rt = new App.RecentTrade();
            rt.amount = amount;
            rt.market = market;
            rt.price = price;
            rt.type = type;
            rt.utctime = time;

            InsertRecentTradeByTime(rt); //Insert the trade by time

            //First check to see if this time has already passed the current candle
            bool[] updatedcandle = new bool[2];
			if (time < App.ChartLastPrice15StartTime)
            {
                //This time is prior to the start of the candle
                updatedcandle[0] = TryUpdateOldCandle(market, Convert.ToDouble(price), time, 0);
                if (updatedcandle[0] == true)
                {
                    App.NebliDexNetLog("Updated a previous 15 minute candle");
                }
                updatedcandle[1] = TryUpdateOldCandle(market, Convert.ToDouble(price), time, 1);
                if (updatedcandle[1] == true)
                {
                    App.NebliDexNetLog("Updated a previous 90 minute candle");
                }
            }

            App.LastPriceObject pr = new App.LastPriceObject();
            pr.price = price;
            pr.market = market;
            pr.atime = time;
            lock (App.ChartLastPrice)
            { //Adding prices is not thread safe
                if (updatedcandle[0] == false)
                {
                    InsertChartLastPriceByTime(App.ChartLastPrice[0], pr);
                }

                if (updatedcandle[1] == false)
                {
                    InsertChartLastPriceByTime(App.ChartLastPrice[1], pr);
                }
            }

			//Update the current candle
			if (market == App.exchange_market && App.main_window_loaded == true)
			{
				Application.Invoke(delegate
				{
					if (updatedcandle[App.main_window.chart_timeline] == false)
					{
					//This most recent candle hasn't been updated yet
					lock (App.ChartLastPrice)
						{
							App.main_window.UpdateLastCandle(Convert.ToDouble(price));
						}
					}
					else
					{
					//Just refresh the view
					lock (App.ChartLastPrice)
						{
							App.main_window.UpdateCandles();
						}
					}
				});
			}

            ExchangeWindow.UpdateOpenOrderList(market, order_nonce); //This will update the views if necessary and remove the order
        }

		public static void InsertChartLastPriceByTime(List<App.LastPriceObject> mylist, App.LastPriceObject lp)
        {
            //The most recent chartlastprice is in the end
            bool inserted = false;
            for (int i = mylist.Count - 1; i >= 0; i--)
            {
                //Insert based on first the time
                App.LastPriceObject plp = mylist[i];
                if (plp.market == lp.market)
                {
                    //Must be same market
                    if (plp.atime < lp.atime)
                    {
                        //Place the last chart time here
                        mylist.Insert(i + 1, lp);
                        inserted = true;
                        break;
                    }
                    else if (plp.atime == lp.atime)
                    {
                        //These trades were made at the same time
                        //Compare the prices
                        if (plp.price > lp.price)
                        {
                            mylist.Insert(i + 1, lp);
                            inserted = true;
                            break;
                        }
                    }
                }
            }
            if (inserted == false)
            {
                mylist.Insert(0, lp); //Add to beginning of the list
            }
        }

        public static void InsertRecentTradeByTime(App.RecentTrade rt)
        {
            //This will insert the recent trade into the list by time (higher first), and then price (lower first)
            //Most recent trade is first on list
            //We will also need to update the NodeStore separately with this code
            lock (App.RecentTradeList[rt.market])
            {
                bool inserted = false;
                for (int i = 0; i < App.RecentTradeList[rt.market].Count; i++)
                {
                    //Insert based on first the time
                    App.RecentTrade prt = App.RecentTradeList[rt.market][i];
                    if (prt.utctime < rt.utctime)
                    {
                        //Place the recent trade here
                        App.RecentTradeList[rt.market].Insert(i, rt);
						int ipos = i;
                        if (App.main_window_loaded == true)
                        {
                            Application.Invoke(delegate
                            {
                                App.main_window.Recent_Trade_List.NodeStore.AddNode(rt, ipos);
                                App.main_window.Recent_Trade_List.ScrollToCell(new TreePath(new int[] { 0 }), null, false, 0, 0); //Put scroller back to top
                            });
                        }
                        inserted = true;
                        break;
                    }
                    else if (prt.utctime == rt.utctime)
                    {
                        //These trades were made at the same time
                        //Compare the prices
                        if (prt.price > rt.price)
                        {
                            App.RecentTradeList[rt.market].Insert(i, rt);
							//Also insert into nodeview
							int ipos = i;
							if (App.main_window_loaded == true)
							{
								Application.Invoke(delegate
								{
									App.main_window.Recent_Trade_List.NodeStore.AddNode(rt, ipos);
									App.main_window.Recent_Trade_List.ScrollToCell(new TreePath(new int[] { 0 }), null, false, 0, 0);
								});
							}
                            inserted = true;
                            break;
                        }
                    }
                }
                if (inserted == false)
                {
                    App.RecentTradeList[rt.market].Add(rt); //Add to end of list, old trade
                    if (App.main_window_loaded == true)
                    {
                        Application.Invoke(delegate
                        {
							//Add the node to the end
                            App.main_window.Recent_Trade_List.NodeStore.AddNode(rt);
							App.main_window.Recent_Trade_List.ScrollToCell(new TreePath(new int[] { 0 }), null, false, 0, 0);
                        });
                    }
				}
            }
        }

		public static bool TryUpdateOldCandle(int market, double price, int time, int timescale)
        {
            //True means successfully updated the candle, no need to put in current candle

            //This will go through the database and update an old candle
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            string table;
            int timeforward = 0;
            if (timescale == 0)
            {
                //24 hour chart
                table = "CANDLESTICKS24H";
                timeforward = 60 * 15;
            }
            else
            {
                table = "CANDLESTICKS7D";
                timeforward = 60 * 90;
            }

            //First update the most recent 24 hour candle
            int backtime = App.UTCTime() - 60 * 60 * 3;
            myquery = "Select highprice, lowprice, open, close, nindex, utctime From " + table + " Where market = @mark And utctime > @time Order By utctime DESC Limit 1"; //Get most recent candle
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.Parameters.AddWithValue("@mark", market);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            double high, low, close, open;
            int nindex = -1;
            if (dataavail == true)
            {
                high = Convert.ToDouble(statement_reader["highprice"].ToString(), CultureInfo.InvariantCulture);
                low = Convert.ToDouble(statement_reader["lowprice"].ToString(), CultureInfo.InvariantCulture);
                open = Convert.ToDouble(statement_reader["open"].ToString(), CultureInfo.InvariantCulture);
                close = Convert.ToDouble(statement_reader["close"].ToString(), CultureInfo.InvariantCulture);
                nindex = Convert.ToInt32(statement_reader["nindex"].ToString());
                int starttime = Convert.ToInt32(statement_reader["utctime"].ToString());
                statement_reader.Close();
                statement.Dispose();
                if (starttime + timeforward > time)
                {
                    //This candle needs to be updated
                    if (price > high)
                    {
                        high = price;
                    }
                    else if (price < low)
                    {
                        low = price;
                    }
                    close = price;
                    myquery = "Update " + table + " Set highprice = @hi, lowprice = @lo, close = @clo Where nindex = @in";
                    statement = new SqliteCommand(myquery, mycon);
                    statement.Parameters.AddWithValue("@hi", high.ToString(CultureInfo.InvariantCulture));
                    statement.Parameters.AddWithValue("@lo", low.ToString(CultureInfo.InvariantCulture));
                    statement.Parameters.AddWithValue("@clo", close.ToString(CultureInfo.InvariantCulture));
                    statement.Parameters.AddWithValue("@in", nindex);
                    statement.ExecuteNonQuery();
                    statement.Dispose();
                    mycon.Close();

                    //Candle was updated
                    return true;
                }
            }
            else
            {
                //No candle exists      
            }
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
            return false;
        }

		public static void ClearMarketData(int market)
        {
            //Remove all market data for our market
            //This function is not performed by critical nodes
            //Market -1 means remove all markets data if there

            //Clear all the candles
            string myquery;
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Delete the Candles Database as they have come out of sync and obtain new chart from another server
            myquery = "Delete From CANDLESTICKS7D";
            statement = new SqliteCommand(myquery, mycon);
            statement.ExecuteNonQuery();
            statement.Dispose();

            myquery = "Delete From CANDLESTICKS24H";
            statement = new SqliteCommand(myquery, mycon);
            statement.ExecuteNonQuery();
            statement.Dispose();
            mycon.Close();

            //Now clear the open orders for the market
            if (market > 0)
            {
                lock (App.OpenOrderList[market])
                {
                    App.OpenOrderList[market].Clear(); //We don't need to see those orders
                }
                lock (App.RecentTradeList[market])
                {
                    App.RecentTradeList[market].Clear();
                }
            }
            else
            {
                for (int mark = 0; mark < App.total_markets; mark++)
                {
                    lock (App.OpenOrderList[mark])
                    {
                        App.OpenOrderList[mark].Clear(); //We don't need to see those orders
                    }
                    lock (App.RecentTradeList[mark])
                    {
                        App.RecentTradeList[mark].Clear();
                    }
                }
            }
            lock (App.ChartLastPrice)
            {
                App.ChartLastPrice[0].Clear();
                App.ChartLastPrice[1].Clear();
            }

        }

        public void SetupButtons()
		{
			Chart_Canvas_Background.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
			Market_Percent_Background.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
            Wallet_Box.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(246, 240, 251));
            Trade_Information_Tabs.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(246, 240, 251));
            Wallet_Box_Frame.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(172, 172, 172));
            Buying_View_Scroller.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(172, 172, 172));
            Selling_View_Scroller.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(172, 172, 172));
            Market_Percent.ModifyFg(Gtk.StateType.Normal, gdk_white);
            //Need to change HBox to eventbox to get background

            Gtk.Label buy_button_label = (Gtk.Label)Buy_Button.Children[0];
			buy_button_label.Markup = "<span font='12.5'>Buy " + App.MarketList[App.exchange_market].trade_symbol +"</span>";
            Gtk.Label sell_button_label = (Gtk.Label)Sell_Button.Children[0];
			sell_button_label.Markup = "<span font='12.5'>Sell " + App.MarketList[App.exchange_market].trade_symbol + "</span>";

            Gtk.Label withdraw_button_label = (Gtk.Label)Withdraw_Button.Children[0];
            withdraw_button_label.Markup = "<span font='10'>Withdraw</span>";
            Gtk.Label deposit_button_label = (Gtk.Label)Deposit_Button.Children[0];
            deposit_button_label.Markup = "<span font='10'>Deposit</span>";

            Chart_Time_Toggle.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
			Chart_Time_Toggle.ModifyBg(Gtk.StateType.Prelight, new Gdk.Color(30, 54, 74));
			Chart_Time_Toggle.ModifyBg(Gtk.StateType.Active, new Gdk.Color(50, 74, 94));
			Gtk.Label chart_time_label = (Gtk.Label)Chart_Time_Toggle.Children[0];
			chart_time_label.ModifyFg(Gtk.StateType.Normal, gdk_white);
			chart_time_label.ModifyFg(Gtk.StateType.Prelight, gdk_white);
			chart_time_label.ModifyFg(Gtk.StateType.Active, gdk_white);
            chart_time_label.Markup = "<span font='10'>24 Hour</span>";

            Chart_Mouse_Price.ModifyFg(Gtk.StateType.Normal, gdk_white);
            Chart_Last_Price.ModifyFg(Gtk.StateType.Normal, gdk_white);
			Chart_Canvas.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
			Chart_Mouse_Price.Text = "";

			//Now create all the headers for all the NodeViews
			NodeStore Recent_Trade_Nodes = new NodeStore(typeof(App.RecentTrade));
			//Recent_Trade_Nodes
			Recent_Trade_List.NodeStore = Recent_Trade_Nodes;
            //Must use reflection since NodeStore set doesn't work
			typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Recent_Trade_List, Recent_Trade_Nodes);
			Recent_Trade_List.AppendColumn("Zulu Time", new Gtk.CellRendererText(), "text", 0);
			Recent_Trade_List.AppendColumn("Type", new Gtk.CellRendererText(), "text", 1);
			Recent_Trade_List.AppendColumn("Price", new Gtk.CellRendererText(), "text", 2);
			Recent_Trade_List.AppendColumn("Amount", new Gtk.CellRendererText(), "text", 3);

			NodeStore My_Trade_Nodes = new NodeStore(typeof(App.MyTrade));
            //My_Trade_Nodes
            Trade_History_List.NodeStore = My_Trade_Nodes;
            //Must use reflection since NodeStore set doesn't work
			typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Trade_History_List, My_Trade_Nodes);
			Trade_History_List.AppendColumn("Date", new Gtk.CellRendererText(), "text", 0);
			Trade_History_List.AppendColumn("Pair", new Gtk.CellRendererText(), "text", 1);
			Trade_History_List.AppendColumn("Type", new Gtk.CellRendererText(), "text", 2);
			Trade_History_List.AppendColumn("Price", new Gtk.CellRendererText(), "text", 3);
			Trade_History_List.AppendColumn("Amount", new Gtk.CellRendererText(), "text", 4);

			NodeStore My_Open_Orders_Nodes = new NodeStore(typeof(App.OpenOrder));
			//My_Open_Order_Nodes
			Open_Orders_List.NodeStore = My_Open_Orders_Nodes;
            //Must use reflection since NodeStore set doesn't work
            typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Open_Orders_List, My_Open_Orders_Nodes);
			Open_Orders_List.AppendColumn("Pair", new Gtk.CellRendererText(), "text", 0);
			Open_Orders_List.AppendColumn("Type", new Gtk.CellRendererText(), "text", 1);
			Open_Orders_List.AppendColumn("Price", new Gtk.CellRendererText(), "text", 2);
			Open_Orders_List.AppendColumn("Amount", new Gtk.CellRendererText(), "text", 10);
			Open_Orders_List.AppendColumn("% Filled", new Gtk.CellRendererText(), "text", 4);
			Gtk.CellRendererToggle toggle_cancel_rend = new Gtk.CellRendererToggle();
			toggle_cancel_rend.Activatable = true;
			toggle_cancel_rend.Toggled += Request_Cancel_Order;
			Open_Orders_List.AppendColumn("Cancel", toggle_cancel_rend,"active",6,"visible",7);

			//Connect to public interface
			Open_Order_List_Public = Open_Orders_List;
			Trade_History_List_Public = Trade_History_List;
			Recent_Trade_List_Public = Recent_Trade_List;
			CN_Tx_List_Public = CN_Tx_List;
			Wallet_View_Public = Wallet_View;

			NodeStore CN_Tx_List_Nodes = new NodeStore(typeof(App.MyCNFee));
			//CN Fee List
			CN_Tx_List.NodeStore = CN_Tx_List_Nodes;
            //Must use reflection since NodeStore set doesn't work
			typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(CN_Tx_List, CN_Tx_List_Nodes);
			CN_Tx_List.AppendColumn("Date", new Gtk.CellRendererText(), "text", 0);
			CN_Tx_List.AppendColumn("Pair", new Gtk.CellRendererText(), "text", 1);
			CN_Tx_List.AppendColumn("Fee Earned", new Gtk.CellRendererText(), "text", 2);

			NodeStore Sell_Order_Nodes = new NodeStore(typeof(App.OpenOrder));
			//Sell Orders
			Selling_View.NodeStore = Sell_Order_Nodes;
            //Must use reflection since NodeStore set doesn't work
			typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Selling_View, Sell_Order_Nodes);
			Selling_View.AppendColumn("Price", new Gtk.CellRendererText(), "text", 2,"sensitive",8,"height",9);
			Selling_View.AppendColumn("Amount", new Gtk.CellRendererText(), "text", 3,"sensitive",8,"height",9);
			Selling_View.AppendColumn("Total", new Gtk.CellRendererText(), "text", 5,"sensitive",8,"height",9);
			Selling_View.CursorChanged += Check_Row_Selection;
			//We need to anchor the rows to the bottom
			//We are doing a hacky workaround to attach the nodes to the bottom

            NodeStore Buy_Order_Nodes = new NodeStore(typeof(App.OpenOrder));
            //Buy Orders
            Buying_View.NodeStore = Buy_Order_Nodes;
            //Must use reflection since NodeStore set doesn't work
            typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Buying_View, Buy_Order_Nodes);
			Buying_View.AppendColumn("Price", new Gtk.CellRendererText(), "text", 2);
			Buying_View.AppendColumn("Amount", new Gtk.CellRendererText(), "text", 3);
			Buying_View.AppendColumn("Total", new Gtk.CellRendererText(), "text", 5);
			Buying_View.FixedHeightMode = true;

			NodeStore Wallet_Nodes = new NodeStore(typeof(App.Wallet));
			//Buy Orders
			Wallet_View.NodeStore = Wallet_Nodes;
            //Must use reflection since NodeStore set doesn't work
            typeof(NodeView).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Wallet_View, Wallet_Nodes);
			Wallet_View.AppendColumn("Coin", new Gtk.CellRendererText(), "text", 0);
			Wallet_View.AppendColumn("Amount", new Gtk.CellRendererText(), "text", 1);
			Wallet_View.AppendColumn("Status", new Gtk.CellRendererText(), "text", 2);

            //Now adjust the Cell renderers fonts and colors
			Gtk.CellRendererText my_rend = (Gtk.CellRendererText)Wallet_View.Columns[0].CellRenderers[0];
			my_rend.Weight = 1000;
			my_rend = (Gtk.CellRendererText)Selling_View.Columns[0].CellRenderers[0];
            my_rend.Weight = 1000;
			my_rend.ForegroundGdk = candle_redc;
			my_rend = (Gtk.CellRendererText)Buying_View.Columns[0].CellRenderers[0];
            my_rend.Weight = 1000;
			my_rend.ForegroundGdk = candle_darkgreenc;
			my_rend = (Gtk.CellRendererText)Market_Box.Cells[0];
			my_rend.Scale = 1.4;
			Market_Box.WrapWidth = 5;
			my_rend = (Gtk.CellRendererText)Open_Orders_List.Columns[4].CellRenderers[0];
            my_rend.Weight = 1000;

            //We have to manually with code, set the proportions and keep updating it
            int win_width = this.DefaultWidth;
            int win_height = this.DefaultHeight;

            //Adjust the views to match the window size
            Selling_View_Scroller.WidthRequest = (int)Math.Round((double)win_width * 0.29);
            Buying_View_Scroller.WidthRequest = (int)Math.Round((double)win_width * 0.29);
			Chart_Canvas.SetSizeRequest(win_width - (int)Math.Round((double)win_width * 0.29) - 25, (int)Math.Round((double)win_height * 0.30));

			//Set up the chart_canvas drawing objects
			Chart_Canvas.ExposeEvent += Draw_Canvas;
			Chart_Canvas.AddEvents((int)Gdk.EventMask.PointerMotionMask);
			Chart_Canvas.MotionNotifyEvent += Chart_MouseMove;

			//Add event handlers to the buttons
			Buy_Button.Clicked += Open_Buy;
			Sell_Button.Clicked += Open_Sell;
			Deposit_Button.Clicked += Open_Deposit;
			Withdraw_Button.Clicked += Open_Withdraw;
			Market_Box.Changed += Change_Market;
			Chart_Time_Toggle.Clicked += Change_Chart_Timeline;
			BackupWalletAction.Activated += Save_Backup_Dialog;
			ImportNebliDexWalletAction.Activated += Open_Backup_Dialog;
			ChangeWalletAddressesAction.Activated += Request_Change_Address;
			EncryptWalletAction.Activated += Toggle_Encryption;
			ActivateAsCriticalNodeAction.Activated += Request_CN_Status;
			ActivateTraderAPIAction.Activated += Toggle_Trader_API;
			Selling_View.RowActivated += Select_Order;
			Buying_View.RowActivated += Select_Order;
			Selling_View.CursorChanged += Reset_AutoScroll;
			Buying_View.CursorChanged += Reset_AutoScroll;
			//DefaultTheme_Action.Active = true;

			//Setup the trayicon
			trayicon = new StatusIcon(App.App_Path + "/logo.ico");
			trayicon.Visible = false; //Hide it for now
			trayicon.Activate += delegate { this.Show(); trayicon.Visible = false; };
			RunInBackgroundAction1.Activated += delegate { this.Hide(); trayicon.Visible = true; };
			trayicon.Tooltip = "NebliDex will continue to run in the background to keep your open orders from closing.";

			//Hide the CN Fees Tab and Menu Option unless CN
			ToggleCNInfo(false);

			//Adjust the table headers
			AdjustTableHeaders();

		}

        public void ModifyWindowProp(object obj, EventArgs arg)
		{
			Gdk.Rectangle win_size = this.Allocation;
			int win_width = win_size.Width;
			int win_height = win_size.Height;
			if (win_width < 0) { return; }
			if(window_width <= 0){
				//Just in case
				window_width = win_width;
				window_height = win_height;
				return;
			}

			if (window_width != win_width || window_height != win_height)
            {
				double scalefactor = (double)win_width / (double)window_width;
                AdjustTableHeaders(scalefactor);          
            }
            
			Selling_View_Scroller.WidthRequest = (int)Math.Round((double)win_width * 0.29);
            Buying_View_Scroller.WidthRequest = (int)Math.Round((double)win_width * 0.29);       
			Chart_Canvas.SetSizeRequest(win_width - (int)Math.Round((double)win_width * 0.29) - 25, (int)Math.Round((double)win_height * 0.30));

			window_width = win_width;
			window_height = win_height;

			int sell_height = Selling_View.Allocation.Height;
			if(sell_height != sellingview_height){
				//Selling View has changed
				RemovePaddingForOrderView(Selling_View);
                int row_num = NodeViewRowCount(Selling_View);
                AddPaddingForOrderView(Selling_View, row_num);
				sellingview_height = sell_height;
			}
         
		}

		public static void PeriodicCandleMaker(object state)
        {
            //Update the candles at this set interval

            //Now move the candle watching time forward
            int utime = App.UTCTime();
            if (App.next_candle_time == 0)
            {
                App.next_candle_time = utime + 60 * 15;
            }
            else
            {
                App.next_candle_time += 60 * 15;
            }

            //Set the 15 minute candle time to this
            App.ChartLastPrice15StartTime = App.next_candle_time - 60 * 15;

            //Because System timers are inprecise and lead to drift, we must update time manually
            int waittime = App.next_candle_time - utime;
            if (waittime < 0) { waittime = 0; } //Shouldn't be possible
			if (App.CandleTimer == null) { return; }
            App.CandleTimer.Change(waittime * 1000, System.Threading.Timeout.Infinite);

            lock (App.ChartLastPrice)
            {
                string myquery = "";
                double high, low, open, close;

                App.candle_15m_interval++;
                int end_time = 1;
                if (App.candle_15m_interval == 6)
                {
                    App.candle_15m_interval = 0;
                    end_time = 2;
                    //Create a candle using our lastpriceobject list for each market (if CN) or 1 market for regular node
                }

                int start_market = 0;
                int end_market = App.total_markets;
                if (App.critical_node == false)
                {
                    start_market = App.exchange_market; //Only store for
                    end_market = start_market + 1;
                }

                //CNs are only ones required to store all candle data from all markets
                //Do the same for the 15 minute lastpriceobject
                //Create a transaction
                SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
                mycon.Open();

                //Set our busy timeout, so we wait if there are locks present
                SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
                statement.ExecuteNonQuery();
                statement.Dispose();

                statement = new SqliteCommand("BEGIN TRANSACTION", mycon); //Create a transaction to make inserts faster
                statement.ExecuteNonQuery();
                statement.Dispose();

                for (int time = 0; time < end_time; time++)
                {
                    for (int market = start_market; market < end_market; market++)
                    {
                        //Go through the 7 day table if necessary
                        int numpts = App.ChartLastPrice[time].Count; //All the pounts for the timeline
                        open = -1;
                        close = -1;
                        high = -1;
                        low = -1;

                        for (int pos = 0; pos < App.ChartLastPrice[time].Count; pos++)
                        { //This should be chronological
							if (App.ChartLastPrice[time][pos].market == market)
                            {
                                double price = Convert.ToDouble(App.ChartLastPrice[time][pos].price);
                                if (open < 0) { open = price; }
                                if (price > high)
                                {
                                    high = price;
                                }
                                if (low < 0 || price < low)
                                {
                                    low = price;
                                }
                                close = price; //The last price will be the close
                                               //Which will also be the open for the next candle
                            }
                        }

                        //Then delete all the ones except the last one
                        bool clear = false;
                        //Reset all chart last prices
                        //Remove all the prices except the last one, this will be our new open
                        for (int pos = App.ChartLastPrice[time].Count - 1; pos >= 0; pos--)
                        {
							if (App.ChartLastPrice[time][pos].market == market)
                            {
                                if (clear == false)
                                {
                                    //This is the one to save, most recent lastpriceobject
                                    clear = true;
                                    close = Convert.ToDouble(App.ChartLastPrice[time][pos].price);
                                }
                                else if (clear == true)
                                {
                                    //Remove all else
                                    App.ChartLastPrice[time].RemoveAt(pos); //Take it out
                                }
                            }
                        }

                        if (market == App.exchange_market && App.main_window_loaded == true)
                        {
                            //Now if this is the active market, add a new candle based on the charttimeline
                            //Now modify the visible candles
                            if (App.VisibleCandles.Count > 0 && close > 0)
                            {
                                if (App.main_window.chart_timeline == 0 && time == 0)
                                {
                                    //24 Hr                           
                                    App.Candle can = new App.Candle(close);
									Application.Invoke(delegate
                                    {
										App.main_window.PlaceCandleInChart(can);
                                    });
                                }
                                else if (App.main_window.chart_timeline == 1 && time == 1)
                                {
                                    //Only place new candle on this timeline every 90 minutes
                                    App.Candle can = new App.Candle(close);
									Application.Invoke(delegate
                                    {
                                        App.main_window.PlaceCandleInChart(can);
                                    });
                                }
                            }
                        }

                        if (open > 0)
                        {
                            //May not have any activity on that market yet
                            //If there is at least 1 trade on market, this will add to database
                            //Insert old candle into database
                            int ctime = App.UTCTime();
                            if (time == 0)
                            {
                                ctime -= 60 * 15; //This candle started 15 minutes ago
                                myquery = "Insert Into CANDLESTICKS24H";
                            }
                            else if (time == 1)
                            {
                                ctime -= 60 * 90; //This candle started 90 minutes ago
                                myquery = "Insert Into CANDLESTICKS7D";
                            }

                            //Insert to the candle database

                            myquery += " (utctime, market, highprice, lowprice, open, close)";
                            myquery += " Values (@time, @mark, @high, @low, @op, @clos);";
                            statement = new SqliteCommand(myquery, mycon);
                            statement.Parameters.AddWithValue("@time", ctime);
                            statement.Parameters.AddWithValue("@mark", market);
                            statement.Parameters.AddWithValue("@high", high);
                            statement.Parameters.AddWithValue("@low", low);
                            statement.Parameters.AddWithValue("@op", open);
                            statement.Parameters.AddWithValue("@clos", close);
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                        }
                    }
                }


                //Close the connection
                statement = new SqliteCommand("COMMIT TRANSACTION", mycon); //Close the transaction
                statement.ExecuteNonQuery();
                statement.Dispose();
                mycon.Close();

            }

            //Now calculate the fee as the chart has changed and send to all connected TNs
            //Calculate the CN Fee
            if (App.critical_node == true)
            {
                App.CalculateCNFee();
                //Push this new fee to all connected pairs
                lock (App.DexConnectionList)
                {
                    for (int i = 0; i < App.DexConnectionList.Count; i++)
                    {
                        if (App.DexConnectionList[i].outgoing == false && App.DexConnectionList[i].contype == 3 && App.DexConnectionList[i].version >= App.protocol_min_version)
                        {
                            App.SendCNServerAction(App.DexConnectionList[i], 53, "");
                        }
                    }
                }

                //If a critical node is pending right now, we must resync the chart again because candle data may be inaccurate
                if (App.critical_node_pending == true)
                {
                    App.NebliDexNetLog("Resyncing candles because potential candle information lost");
                    App.reconnect_cn = true;
                    App.cn_network_down = true; //This will force a resync
                }

				if (App.run_headless == true)
                {
					//This is a heartbeat indicator
					if (App.trader_api_activated == false)
                    {
                        int cn_online = App.CN_Nodes_By_IP.Count;
                        string percent = String.Format(CultureInfo.InvariantCulture, "{0:0.###}", Math.Round(App.my_cn_weight * 100, 3));
                        Console.WriteLine("Critical Node Status (" + App.UTCTime() + " UTC TIME): CNs Online: " + cn_online + ", " + percent + "% Chance of Validating");
                    }
                    else
                    {
                        Console.WriteLine("Trader API server running (" + App.UTCTime() + " UTC TIME) with " + App.MyOpenOrderList.Count + " open order(s) present");
                    }
                }
            }

            //Finally check the electrum server sync
            App.CheckElectrumServerSync();
			App.CheckCNBlockHelperServerSync();
        }
        
        private void Draw_Canvas(object sender, ExposeEventArgs args)
		{
			
			Gdk.Window draw_win = args.Event.Window;
			int buf_width = 0;
			int buf_height = 0;
			draw_win.GetSize(out buf_width, out buf_height);
			using (Cairo.Context draw_buf = Gdk.CairoHelper.Create(draw_win))
			{
				DrawCandlePositions(draw_buf,buf_width,buf_height);
				draw_buf.Dispose(); //Must flush buffer eventually
			}
		}

        private void AdjustTableHeaders(double scale_xfactor=1)
		{
			int colm = Selling_View.Columns.Length;
			for (int i = 0; i < colm;i++){
				TreeViewColumn mycol = Selling_View.Columns[i];
				mycol.Alignment = 0.5f;
				int view_size = (int)Math.Round((double)Selling_View.Allocation.Width*scale_xfactor) - 20;
                mycol.Expand = true;
                int space = (int)Math.Round((double)view_size / (double)colm);

                mycol.Sizing = TreeViewColumnSizing.Fixed;
                mycol.FixedWidth = space;
				mycol.CellRenderers[0].Height = 23;
			}

			colm = Buying_View.Columns.Length;
            for (int i = 0; i < colm; i++)
            {
                TreeViewColumn mycol = Buying_View.Columns[i];
				int view_size = (int)Math.Round((double)Buying_View.Allocation.Width*scale_xfactor) - 20;
                mycol.Expand = true;
                int space = (int)Math.Round((double)view_size / (double)colm);

                mycol.Sizing = TreeViewColumnSizing.Fixed;
                mycol.FixedWidth = space;
				mycol.CellRenderers[0].Height = 23;
            }

			colm = Trade_History_List.Columns.Length;
            for (int i = 0; i < colm; i++)
            {
                TreeViewColumn mycol = Trade_History_List.Columns[i];
				Gtk.CellRendererText my_rend = (Gtk.CellRendererText)mycol.CellRenderers[0];
                my_rend.Xalign = 0.5f;
				mycol.Alignment = 0.5f;
                mycol.Expand = true;
            }

			colm = Recent_Trade_List.Columns.Length;
            for (int i = 0; i < colm; i++)
            {
                TreeViewColumn mycol = Recent_Trade_List.Columns[i];
				Gtk.CellRendererText my_rend = (Gtk.CellRendererText)mycol.CellRenderers[0];
				my_rend.Xalign = 0.5f;
				mycol.Alignment = 0.5f;
                mycol.Expand = true;
            }

			colm = Open_Orders_List.Columns.Length;
            for (int i = 0; i < colm; i++)
            {
                TreeViewColumn mycol = Open_Orders_List.Columns[i];
				Gtk.CellRenderer my_rend = (Gtk.CellRenderer)mycol.CellRenderers[0];
                my_rend.Xalign = 0.5f;
				mycol.Alignment = 0.5f;
                mycol.Expand = true;
            }

			colm = CN_Tx_List.Columns.Length;
            for (int i = 0; i < colm; i++)
            {
                TreeViewColumn mycol = CN_Tx_List.Columns[i];
				Gtk.CellRendererText my_rend = (Gtk.CellRendererText)mycol.CellRenderers[0];
                my_rend.Xalign = 0.5f;
				mycol.Alignment = 0.5f;
                mycol.Expand = true;
            }

			colm = Wallet_View.Columns.Length;
            for (int i = 0; i < colm; i++)
            {
                TreeViewColumn mycol = Wallet_View.Columns[i];
				mycol.Alignment = 0.5f;
				int view_size = (int)Math.Round((double)Wallet_View.Allocation.Width*scale_xfactor)-20;
				mycol.Expand = true;
				double percent = 0.24;
				if(i == 1){
					percent = 0.49;
				}
				int space = (int)Math.Round((double)view_size * percent);

                mycol.Sizing = TreeViewColumnSizing.Fixed;
				mycol.FixedWidth = space;
            }
		}

		private void Change_Chart_Timeline(object sender, EventArgs e)
        {
            //Change Chart Timeline and reload
            if (Convert.ToString(Market_Percent.Text) == "LOADING...") { return; }
			Gtk.Label chart_time_label = (Gtk.Label)Chart_Time_Toggle.Children[0];
            lock (App.ChartLastPrice)
            { //Do not touch the visible candles unless we know no one else is
				if ((string)chart_time_label.Text == "24 Hour")
                {
                    //Change to 7 Day
					chart_time_label.Markup = "<span font='10'>7 Day</span>";
                    chart_timeline = 1;
                }
                else
                {
					chart_time_label.Markup = "<span font='10'>24 Hour</span>";
                    chart_timeline = 0;
                }
                UpdateCandles();
            }
        }

		private async void Change_Market(object sender, EventArgs args)
        {
            if (App.main_window_loaded == false) { return; }
            if (Convert.ToString(Market_Percent.Text) == "LOADING...") { return; }  //Can't change market when waiting

			//First find which one was selected
			string market_string = Market_Box.ActiveText;
			int which_market = Selected_Market(market_string); //Will find the numeric market based on the formatted string

            if (which_market > -1)
            {
                int oldmarket = App.exchange_market;
                App.exchange_market = which_market;
				if (oldmarket == App.exchange_market) { return; } //No change

				App.trader_api_changing_markets = true; // In case Trading API being used
				Market_Box.Sensitive = false;
                if (App.critical_node == false)
                {
					Market_Percent.Markup = "<span font='13'><b>LOADING...</b></span>"; //Put a loading status
					Market_Percent.ModifyFg(Gtk.StateType.Normal, gdk_white);
					if (current_ui_look == 1)
                    {
                        Market_Percent.ModifyFg(Gtk.StateType.Normal, blackc);
					}else if (current_ui_look == 2)
                    {
                        Market_Percent.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
                    }
                    //Clear the old candles and charts and reload the market data for this market
                    await Task.Run(() => { ClearMarketData(oldmarket); App.GetCNMarketData(App.exchange_market); });
                }
				Application.Invoke(delegate
                {
					//Ran after async so must be invoked into UI thread
					Market_Box.Sensitive = true; //Re-enable the box
                    RefreshUI();
                    Save_UI_Config(); //Save the UI Market Info
                });
				App.trader_api_changing_markets = false;
            }
        }

		public void Change_Market_Info_Only(string market, bool loading)
        {
            // This only updates the Market Box and the Market percent window
			Application.Invoke(delegate
            {
				// Must go through the TreeModel to find out the values inside the ComboBox
                TreeModel model = Market_Box.Model;
                TreeIter iter;
                if (model.GetIterFirst(out iter))
                {
					//There is something here

                    do
                    {                  
						string market_string = Market_Box.Model.GetValue(iter,0) as string;
                        if (market_string.Equals(market))
                        {
                            Market_Box.SetActiveIter(iter); // Change the selected index
                            break;
                        }                       
                    } while (model.IterNext(ref iter));
                }
                if (loading == true)
                {
					Market_Percent.Markup = "<span font='13'><b>LOADING...</b></span>"; //Put a loading status
                    Market_Percent.ModifyFg(Gtk.StateType.Normal, gdk_white);
                    if (current_ui_look == 1)
                    {
                        Market_Percent.ModifyFg(Gtk.StateType.Normal, blackc);
                    }
                    else if (current_ui_look == 2)
                    {
                        Market_Percent.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
                    }
					Market_Box.Sensitive = false;
                }
                else
                {
                    // No longer loading
					Market_Box.Sensitive = true;
                    RefreshUI();
                    Save_UI_Config(); //Save the UI Market Info
                }
            });
        }

		private int Selected_Market(string mform)
        {
            for (int i = 0; i < App.MarketList.Count; i++)
            {
                if (mform == App.MarketList[i].trade_symbol + "/" + App.MarketList[i].base_symbol)
                {
                    return i;
                }
            }
            return -1;
        }

		private void Save_Backup_Dialog(object sender, EventArgs e)
        {
			ResponseType response = ResponseType.None;
			Gtk.FileChooserDialog dia = null;
			string filename = "";
			try
            {
				dia = new FileChooserDialog("Choose Filename To Copy NebliDex Wallet File", this, FileChooserAction.Save, "Save", ResponseType.Ok, "Cancel", ResponseType.Cancel);
				FileFilter filter = new FileFilter();
                filter.Name = "NebliDex Wallet File";
                filter.AddPattern("*.dat");
				dia.Filter = filter;
				dia.SetFilename("account.dat");
				response = (ResponseType)dia.Run();
				filename = dia.Filename;
            }
            finally
            {
                if (dia != null)
                {
                    dia.Destroy();
                }
            }

            if (response == ResponseType.Ok)
            {
                //Make a copy of the wallet file here
                try
                {
                    File.Copy(App.App_Path + "/data/account.dat", filename);
                }
                catch (Exception)
                {
                    App.NebliDexNetLog("Failed to create wallet backup");
                }
            }
        }

		private async void Open_Backup_Dialog(object sender, EventArgs e)
        {

            if (App.MyOpenOrderList.Count > 0)
            {
				App.MessageBox(this,"Notice","Cannot load wallet with open orders present.","OK");
                return;
            }
            if (App.critical_node == true)
            {
				App.MessageBox(this, "Notice","Cannot load wallet while as a critical node.","OK");
                return;
            }
            if (App.CheckPendingPayment() == true)
            {
				App.MessageBox(this, "Notice","There is at least one pending payment to this current address.","OK");
                return;
            }

            bool moveable = true;
            for (int i = 0; i < App.WalletList.Count; i++)
            {
                if (App.WalletList[i].status != 0)
                {
                    moveable = false; break;
                }
            }
            if (moveable == false)
            {
				App.MessageBox(this, "Notice","There is at least one wallet unavailable to change the current address", "OK");
                return;
            }

			bool result = App.PromptUser(this, "Confirmation", "When you import a new wallet, your current wallet will be replaced.", "Continue", "Cancel");
            if (result == false)
            {
				return;
            }

			ResponseType response = ResponseType.None;
            Gtk.FileChooserDialog dia = null;
            string filename = "";
            try
            {
                dia = new FileChooserDialog("Import NebliDex Wallet File", this, FileChooserAction.Open, "Import", ResponseType.Ok, "Cancel", ResponseType.Cancel);
				FileFilter filter = new FileFilter();
                filter.Name = "NebliDex Wallet File";
                filter.AddPattern("*.dat");
                dia.Filter = filter;
                dia.SetFilename("account.dat");
                response = (ResponseType)dia.Run();
                filename = dia.Filename;
            }
            finally
            {
                if (dia != null)
                {
                    dia.Destroy();
                }
            }

            if (response == ResponseType.Ok)
            {
                //Move all the files around to this new wallet and load it
                try
                {
                    if (File.Exists(App.App_Path + "/data/account_old.dat") != false)
                    {
                        File.Delete(App.App_Path + "/data/account_old.dat");
                    }
                    //Move the account.dat to the new location (old file) until the copy is complete
                    File.Move(App.App_Path + "/data/account.dat", App.App_Path + "/data/account_old.dat");
                    if (File.Exists(filename) == false)
                    {
                        //Revert the changes
                        File.Move(App.App_Path + "/data/account_old.dat", App.App_Path + "/data/account.dat");
						App.MessageBox(this, "Notice","Unable to import this wallet location.","OK");
                        return;
                    }
                    File.Copy(filename, App.App_Path + "/data/account.dat");
                    App.my_wallet_pass = ""; //Remove the wallet password
					await Task.Run(() => App.CheckWallet(this)); //Ran off UI thread
                    await Task.Run(() => App.LoadWallet());
					Application.Invoke(delegate
                    {
						Wallet_View.NodeStore.Clear();
						for (int i = 0; i < App.WalletList.Count; i++)
                        {
                            Wallet_View.NodeStore.AddNode(App.WalletList[i]);
                        }
                    });
                    //Now delete the old wallet
                    File.Delete(App.App_Path + "/data/account_old.dat");
                }
                catch (Exception)
                {
                    App.NebliDexNetLog("Failed to load wallet");
					Application.Invoke(delegate
                    {
						App.MessageBox(this, "Notice", "Failed to load imported NebliDex wallet.", "OK");
                    });
                }
            }
        }

		private async void Request_Change_Address(object sender, EventArgs e)
        {
            //This will request a change to all the addresses
            if (App.MyOpenOrderList.Count > 0)
            {
				App.MessageBox(this, "Notice","Cannot change addresses with open orders present.","OK");
                return;
            }
            if (App.critical_node == true)
            {
				App.MessageBox(this, "Notice","Cannot change addresses while as a critical node.","OK");
                return;
            }

			bool result = App.PromptUser(this,"Confirmation","Are you sure you want to change all your wallet addresses?","Yes","No");
            if (result == true)
            {
                await Task.Run(() => App.ChangeWalletAddresses());
            }
        }

		private void Request_ClearCNData(object sender, EventArgs e)
        {
            //This will request a CN table to be cleared
			bool result = App.PromptUser(this, "Confirmation", "Are you sure you want to clear your CN fees history?", "Yes", "No");
            if (result == true)
            {
                App.ClearAllCNFees();
            }
        }

		private async void Toggle_Encryption(object sender, EventArgs e)
        {
            //This will bring up the prompt to encrypt or decrypt the wallet
            if (App.my_wallet_pass.Length > 0)
            {
                //Encryption is present
				UserPromptWindow p = new UserPromptWindow("Please enter your wallet password\nto decrypt wallet.", true); //Window
                p.Parent = this;
                p.Modal = true;
				p.waiting = new ManualResetEvent(false);
                p.Show();

                //Now create a task and wait for it to return until after the form is closed
				await Task.Run(() => { p.waiting.WaitOne(); });

                if (p.final_response.Equals(App.my_wallet_pass) == false)
                {
					Application.Invoke(delegate
                    {
						App.MessageBox(this, "Error!", "You've entered an incorrect password.", "OK");
                    });
                }
                else
                {
                    App.DecryptWalletKeys();
                    App.my_wallet_pass = "";
					Application.Invoke(delegate
                    {
						EncryptWalletAction.Label = "Encrypt Wallet";
                    });
                }
            }
            else
            {
				UserPromptWindow p = new UserPromptWindow("Please enter a new password\nto encrypt your wallet.", false); //Window
                p.Parent = this;
                p.Modal = true;
                p.waiting = new ManualResetEvent(false);
                p.Show();

				await Task.Run(() => { p.waiting.WaitOne(); }); //Hold the code until the window is closed

                App.my_wallet_pass = p.final_response;
                if (App.my_wallet_pass.Length > 0)
                {
                    App.EncryptWalletKeys();
					Application.Invoke(delegate
                    {
						EncryptWalletAction.Label = "Decrypt Wallet";
                    });
                }
            }
        }

		private async void Request_Cancel_Order(object sender, ToggledArgs args)
        {
			TreePath path = new TreePath(args.Path);
			App.OpenOrder ord = (App.OpenOrder)Open_Orders_List.NodeStore.GetNode(path);
			if (ord == null) { return; }

            if (ord.order_stage >= 3)
            {
                //The maker has an order in which it is waiting for the taker
				App.MessageBox(this, "Notice","Your order is currently involved in a trade. Please try again later.","OK");
                return;
            }
            
			ord.cancel_selected = true;
            await Task.Run(() => App.CancelMyOrder(ord));
			ord.cancel_selected = false;

        }

		private async void Toggle_Trader_API(object sender, EventArgs e)
        {
			if (ActivateTraderAPIAction.Active == App.trader_api_activated)
            {
				//Ran twice by GTK
                return;
            }
            if (App.trader_api_activated == false)
            {
                if (App.critical_node == true)
                {
					App.MessageBox(this, "Notice", "Cannot activate Trader API server while as critical node.", "OK");
					ActivateTraderAPIAction.Active = false;
                    return;
                }
                await Task.Run(() => App.SetTraderAPIServer(true));
				Application.Invoke(delegate
                {
					if (App.trader_api_activated == false)
                    {
						App.MessageBox(this, "Notice", "Failed to activate the Trader API server.", "OK");
						ActivateTraderAPIAction.Active = false;
                    }
                    else
                    {
						App.MessageBox(this, "Notice", "Trader API server now active. Port is " + App.trader_api_port + ".", "OK");
						ActivateTraderAPIAction.Active = true;
                    }
                });
            }
            else
            {
                await Task.Run(() => App.SetTraderAPIServer(false));
				Application.Invoke(delegate
                {
					App.MessageBox(this, "Notice", "Trader API server now deactivated.", "OK");
					ActivateTraderAPIAction.Active = false;
                });
            }
        }

		private async void Request_CN_Status(object sender, EventArgs e)
        {
			if(ActivateAsCriticalNodeAction.Active == App.critical_node){
				//Ran twice by GTK
				return;
			}
            
			if (App.trader_api_activated  == true)
            {
                App.MessageBox(this, "Notice", "Cannot go into Critical Node Mode when Trading API server running.", "OK");
                return;
            }

            if (App.MyOpenOrderList.Count > 0)
            {
				App.MessageBox(this, "Notice","Cannot go into Critical Node Mode with open orders.","OK");
                return;
            }

            //Request CN status from another CN, if none available, alert neblidex.xyz, you are only CN
            //NebliDex will check this claim against blockchain
            IntroWindow win = new IntroWindow();
            win.Parent = this;
			win.Modal = true;
            win.Intro_Status.Text = "";
			win.waiting = new ManualResetEvent(false);
            bool old_critical_node = App.critical_node;
#pragma warning disable
			Task.Run(() => App.ToggleCNStatus(win));
#pragma warning enable

			//Now we will wait for the window to close
			await Task.Run(() => { win.waiting.WaitOne(); });
         
			Application.Invoke(delegate
            {
				//Run all this code on UI thread
				if (App.critical_node == true)
                {
					ToggleCNInfo(true);
                }
                else
                {
					ToggleCNInfo(false);
                }
                if (old_critical_node != App.critical_node)
                {
                    RefreshUI();
                }
            });
        }

        public void ToggleCNInfo(bool active)
		{
			//Will show or Hide the CN info
            if(active == true)
			{
				this.Title = "NebliDex: A Decentralized Neblio Exchange " + App.version_text + " (Critical Node Running)";
                //Hide the CN Fees Tab and Menu Option unless CN
                Widget page = Trade_Information_Tabs.GetNthPage(3);
                page.Show();
                ClearCNDataAction.Visible = true;
				ExportAllCNFeeDataAction.Visible = true;
				CN_Info.Visible = true;
                ActivateAsCriticalNodeAction.Active = true; //Checked
			}else{
				this.Title = "NebliDex: A Decentralized Neblio Exchange " + App.version_text;
                //Hide the CN Fees Tab and Menu Option unless CN
                Widget page = Trade_Information_Tabs.GetNthPage(3);
                page.Hide();
                ClearCNDataAction.Visible = false;
				ExportAllCNFeeDataAction.Visible = false;
				CN_Info.Visible = false;
				CN_Info.Text = "";
                ActivateAsCriticalNodeAction.Active = false; //Not Checked
			}
		}
      
		private void Open_Buy(object sender, EventArgs e)
        {
            if (App.critical_node == true)
            {
                App.MessageBox(this,"Notice","Cannot Create An Order in Critical Node Mode","OK");
                return;
            }
            if (Convert.ToString(Market_Percent.Text) == "LOADING...") { return; } //Cannot buy in between markets

			PlaceOrderWindow window = new PlaceOrderWindow(0);
			window.Parent = this;
			window.Modal = true;
            window.Show();
        }

        private void Open_Sell(object sender, EventArgs e)
        {
            if (App.critical_node == true)
            {
				App.MessageBox(this, "Notice", "Cannot Create An Order in Critical Node Mode", "OK");
                return;
            }
            if (Convert.ToString(Market_Percent.Text) == "LOADING...") { return; } //Cannot buy in between markets
            PlaceOrderWindow window = new PlaceOrderWindow(1);
			window.Parent = this;
            window.Modal = true;
            window.Show();
        }

		private void Open_DNS_Seed(object sender, EventArgs e)
        {
            SeedListWindow dns_seed = new SeedListWindow(App.DNS_SEED);
			dns_seed.Parent = this;
			dns_seed.Modal = true;
			dns_seed.Show();
        }

        private void Open_Deposit(object sender, EventArgs e)
		{
            DepositWindow dep = new DepositWindow();
			dep.Parent = this;
			dep.Modal = true;
			dep.Show();
        }

        private void Open_Withdraw(object sender, EventArgs e)
        {
            WithdrawWindow with = new WithdrawWindow();
			with.Parent = this;
			with.Modal = true;
			with.Show();
        }

        private void AddPaddingForOrderView(NodeView node,int row_num)
		{
			if (row_num == 0) { return; } //Do not add padding, no open orders there
			//This will obtain the cell height, the view height;
			//and calculate how many rows need to be added in order to pin node to bottom
			int cell_height = 25; //Must use fixed heights because GTK is limited
			int view_size = 0;
			node.BinWindow.GetSize(out int width, out view_size); //Bin Window is size of nodeview minus header
			if (row_num * cell_height >= view_size) { return; } //No padding needed
			int diff_height = view_size - (row_num * cell_height) - 2;
			//int cell_num = (int)Math.Ceiling((double)diff_height / (double)cell_height);
         
			//We are just going to add one row with a variable height
			node.NodeStore.AddNode(new App.OpenOrder { filled_node = false, price = 0, row_height = diff_height }, 0);

		}

        private void RemovePaddingForOrderView(NodeView node)
		{
			//This will cycle through the rows and remove the padding
			int rows = NodeViewRowCount(node);
			for (int i = rows - 1; i >= 0; i--)
            {
                TreePath path = new TreePath(new int[] { i }); //Horrible waste of memory but no other option
                App.OpenOrder row_ord = (App.OpenOrder)node.NodeStore.GetNode(path);
                path.Dispose();
				if (row_ord == null) { continue; } //Possible duplicate nodes, skip
				if (row_ord.filled_node == false)
                {
                    //Remove this from the nodestore
                    node.NodeStore.RemoveNode(row_ord);
                }
            }
		}

        private void AddSortedOrderToView(NodeView node,App.OpenOrder ord)
		{
			//Since the views are sorted from highest is first
			TreeModel model = node.Model;
            TreeIter iter;
			TreePath path;
            int count = 0;

            if (model.GetIterFirst(out iter))
            {
                //There is something here
                do
                {
					path = new TreePath(new int[] { count }); //Horrible waste of memory but no other option
					App.OpenOrder old_ord = (App.OpenOrder)node.NodeStore.GetNode(path);
					if (old_ord == ord) { return; } //The order is already in the view, added it will freeze the program
					path.Dispose(); //Improves memory usage
					if(old_ord.price < ord.price && old_ord.filled_node == true){
						//Place our order directly infront of it
						node.NodeStore.AddNode(ord, count);
						return;
					}
					count++;
                } while (model.IterNext(ref iter));
            }
			node.NodeStore.AddNode(ord, count);
		}

        private static int NodeViewRowCount(NodeView node)
		{
			//This will return the total amount of nodes in the view
            //Info is stored in linked list
			TreeModel model = node.Model;
			TreeIter iter;
			int count = 0;
			if(model.GetIterFirst(out iter)){
				//There is something here
				do
				{
					count++;
				} while (model.IterNext(ref iter));
			}
			return count;
		}

		private void Window_Close(object sender, DeleteEventArgs args)
        {
            string msg = "";
            if (App.MyOpenOrderList.Count > 0)
            {
                msg = "You still have an open order. Are you sure you want to exit NebliDex?";
            }
            else if (App.cn_num_validating_tx > 0)
            {
                msg = "You are still validating some transactions. Are you sure you want to exit NebliDex?";
            }

			//Check to see if there are any pending orders that are being matched
            //Since Atomic Swaps are timelocked, it is not advised to leave program when actively involved in swap
            lock (App.MyOpenOrderList)
            {
                for (int i = 0; i < App.MyOpenOrderList.Count; i++)
                {
                    if (App.MyOpenOrderList[i].order_stage >= 3 && App.MyOpenOrderList[i].is_request == false)
                    {
                        msg = "You are involved in a trade. If you close now, you may lose trade amount. Are you sure you want to exit NebliDex?";
                        break;
                    }
                }
            }

            if (App.CheckPendingTrade() == true)
            {
                msg = "You are involved in a trade. If you close now, you may lose trade amount. Are you sure you want to exit NebliDex?";
            }

            //This will be called when the window is closing
            if (msg.Length > 0)
            {
                bool result = App.PromptUser(this, "Confirmation", msg, "Yes", "Cancel");
                if (result == false)
                {
                    if (args != null)
                    {
                        args.RetVal = true;
                    }
                    return;
                }
            }

			App.main_window_loaded = false;
			App.http_open_network = false;
            Application.Quit();
            if (args != null)
            {
                args.RetVal = false;
            }
        }

        private void Exit_Button_Close(object sender, EventArgs args)
        {
            Window_Close(sender, null);
        }

		public void LegalWarning()
        {
            App.MessageBox(this, "DISCLAIMER", "Do not use NebliDex if its use is unlawful in your local jurisdiction.\nCheck your local laws before use.", "OK");
        }

        private async void Save_All_Trades(object sender, EventArgs e)
        {
            ResponseType response = ResponseType.None;
            Gtk.FileChooserDialog dia = null;
            string filename = "";
            try
            {
                dia = new FileChooserDialog("Choose Filename To Save Trade History", this, FileChooserAction.Save, "Save", ResponseType.Ok, "Cancel", ResponseType.Cancel);
                FileFilter filter = new FileFilter();
                filter.Name = "CSV File";
                filter.AddPattern("*.csv");
                dia.Filter = filter;
                dia.SetFilename("tradehistory.csv");
                response = (ResponseType)dia.Run();
                filename = dia.Filename;
            }
            finally
            {
                if (dia != null)
                {
                    dia.Destroy();
                }
            }

            if (response == ResponseType.Ok)
            {
                //Save the file in CSV
                await Task.Run(() => App.ExportTradeHistory(filename));
            }
        }

        private async void Save_All_CNHx(object sender, EventArgs e)
        {
            ResponseType response = ResponseType.None;
            Gtk.FileChooserDialog dia = null;
            string filename = "";
            try
            {
                dia = new FileChooserDialog("Choose Filename To Save CN Fee History", this, FileChooserAction.Save, "Save", ResponseType.Ok, "Cancel", ResponseType.Cancel);
                FileFilter filter = new FileFilter();
                filter.Name = "CSV File";
                filter.AddPattern("*.csv");
                dia.Filter = filter;
                dia.SetFilename("feehistory.csv");
                response = (ResponseType)dia.Run();
                filename = dia.Filename;
            }
            finally
            {
                if (dia != null)
                {
                    dia.Destroy();
                }
            }

            if (response == ResponseType.Ok)
            {
                //Save the file in CSV
                await Task.Run(() => App.ExportCNFeeHistory(filename));
            }
        }

        public async void Prompt_Load_Saved_Orders()
        {
            bool result = App.PromptUser(this, "Load Saved Orders", "Would you like to repost your previously loaded open orders?", "Yes", "No");
            if (result == true)
            {
                await Task.Run(() => App.LoadSavedOrders());
            }
            else
            {
                await Task.Run(() => App.ClearSavedOrders());
            }
        }

		public void Select_UI_Look(object sender, EventArgs e)
        {
			ToggleAction mi = sender as ToggleAction;
            if (mi == DefaultTheme_Action)
            {
				if(DefaultTheme_Action.Active == true){
                    LightTheme_Action.Active = false;
                    DarkTheme_Action.Active = false;
                    if (current_ui_look != 0)
                    {
                        Change_UI_Look(0);
                    }			
				}else{
					if (LightTheme_Action.Active == false && DarkTheme_Action.Active == false)
					{
						DefaultTheme_Action.Active = true;
					}
				}
            }
            else if (mi == LightTheme_Action)
            {
				if (LightTheme_Action.Active == true)
				{
					DefaultTheme_Action.Active = false;
					DarkTheme_Action.Active = false;
					if (current_ui_look != 1)
                    {
                        Change_UI_Look(1);
                    }   
				}else{
					if (DefaultTheme_Action.Active == false && DarkTheme_Action.Active == false)
					{
						LightTheme_Action.Active = true;
					}
				}
            }
            else if (mi == DarkTheme_Action)
            {
				if (DarkTheme_Action.Active == true)
				{
					DefaultTheme_Action.Active = false;
					LightTheme_Action.Active = false;
					if (current_ui_look != 2)
                    {
                        Change_UI_Look(2);
                    }
				}else{
					if (DefaultTheme_Action.Active == false && LightTheme_Action.Active == false)
					{
						DarkTheme_Action.Active = true;
					}
				}
            }
            
        }

		public void Change_UI_Look(int look)
		{
			if(look == 0){
				//Default look
				this.background.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(191, 191, 191));
                //Mac version will probably use a theme from the looks folder too
				Environment.SetEnvironmentVariable("GTK_DATA_PREFIX", default_gtk_dir);
				Gtk.Settings.Default.ThemeName = default_ui_theme;

                //Custom options
				Wallet_Box.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(246, 240, 251));
                Wallet_Box_Frame.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(172, 172, 172));
				Trade_Information_Tabs.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(246, 240, 251));
				Market_Percent_Background.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
                if (Convert.ToString(Market_Percent.Text) == "00.00%")
                {
                    Market_Percent.ModifyFg(Gtk.StateType.Normal, gdk_white);
                }
				if(Market_Percent.Text.IndexOf("+",StringComparison.InvariantCulture) >= 0){
					Market_Percent.ModifyFg(Gtk.StateType.Normal, candle_greenc);
				}
				Chart_Canvas_Background.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
				Chart_Canvas.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
				Chart_Mouse_Price.ModifyFg(Gtk.StateType.Normal, gdk_white);
				Chart_Last_Price.ModifyFg(Gtk.StateType.Normal, gdk_white);
                //Toggle button colors
				Chart_Time_Toggle.ModifyBg(Gtk.StateType.Normal, new Gdk.Color(10, 24, 44));
                Chart_Time_Toggle.ModifyBg(Gtk.StateType.Prelight, new Gdk.Color(30, 54, 74));
                Chart_Time_Toggle.ModifyBg(Gtk.StateType.Active, new Gdk.Color(50, 74, 94));
				Gtk.Label chart_time_label = (Gtk.Label)Chart_Time_Toggle.Children[0];
                chart_time_label.ModifyFg(Gtk.StateType.Normal, gdk_white);
                chart_time_label.ModifyFg(Gtk.StateType.Prelight, gdk_white);
                chart_time_label.ModifyFg(Gtk.StateType.Active, gdk_white);
			}else if(look == 1){
				//Light
				this.background.ModifyBg(Gtk.StateType.Normal, gdk_white);
				Environment.SetEnvironmentVariable("GTK_DATA_PREFIX", App.App_Path+"/looks/");
				Gtk.Settings.Default.ThemeName = "Light";
                
                //Custom options
				Wallet_Box.ModifyBg(Gtk.StateType.Normal, gdk_white);
                Wallet_Box_Frame.ModifyBg(Gtk.StateType.Normal, gdk_white);
				Trade_Information_Tabs.ModifyBg(Gtk.StateType.Normal, gdk_white);
				Market_Percent_Background.ModifyBg(Gtk.StateType.Normal, gdk_white);
				if (Convert.ToString(Market_Percent.Text) == "00.00%")
				{
					Market_Percent.ModifyFg(Gtk.StateType.Normal, blackc);
				}
				if (Market_Percent.Text.IndexOf("+", StringComparison.InvariantCulture) >= 0)
                {
                    Market_Percent.ModifyFg(Gtk.StateType.Normal, candle_darkgreenc);
                }
				Chart_Canvas_Background.ModifyBg(Gtk.StateType.Normal, gdk_white);
                Chart_Canvas.ModifyBg(Gtk.StateType.Normal, gdk_white);
				Chart_Mouse_Price.ModifyFg(Gtk.StateType.Normal, blackc);
				Chart_Last_Price.ModifyFg(Gtk.StateType.Normal, blackc);
                //Toggle button colors
				Chart_Time_Toggle.ModifyBg(Gtk.StateType.Normal, gdk_white);
                Chart_Time_Toggle.ModifyBg(Gtk.StateType.Prelight, new Gdk.Color(220, 220, 220));
				Chart_Time_Toggle.ModifyBg(Gtk.StateType.Active, new Gdk.Color(210, 210, 210));
				Gtk.Label chart_time_label = (Gtk.Label)Chart_Time_Toggle.Children[0];
                chart_time_label.ModifyFg(Gtk.StateType.Normal, blackc);
                chart_time_label.ModifyFg(Gtk.StateType.Prelight, blackc);
                chart_time_label.ModifyFg(Gtk.StateType.Active, blackc);
			}else if(look == 2){
				//Dark
				this.background.ModifyBg(Gtk.StateType.Normal, blackc);
				Environment.SetEnvironmentVariable("GTK_DATA_PREFIX", App.App_Path + "/looks/");
                Gtk.Settings.Default.ThemeName = "Dark";

				//Custom options
                Wallet_Box.ModifyBg(Gtk.StateType.Normal, dark_ui_panel);
				Wallet_Box_Frame.ModifyBg(Gtk.StateType.Normal, dark_ui_panel);
				Trade_Information_Tabs.ModifyBg(Gtk.StateType.Normal, dark_ui_panel);
                Market_Percent_Background.ModifyBg(Gtk.StateType.Normal, blackc);
                if (Convert.ToString(Market_Percent.Text) == "00.00%")
                {
                    Market_Percent.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
                }
				if (Market_Percent.Text.IndexOf("+", StringComparison.InvariantCulture) >= 0)
                {
                    Market_Percent.ModifyFg(Gtk.StateType.Normal, candle_greenc);
                }
				Chart_Canvas_Background.ModifyBg(Gtk.StateType.Normal, dark_ui_panel);
				Chart_Canvas.ModifyBg(Gtk.StateType.Normal, dark_ui_panel);
                Chart_Mouse_Price.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
                Chart_Last_Price.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
                //Toggle button colors
                Chart_Time_Toggle.ModifyBg(Gtk.StateType.Normal, dark_ui_panel);
                Chart_Time_Toggle.ModifyBg(Gtk.StateType.Prelight, dark_ui_panel);
                Chart_Time_Toggle.ModifyBg(Gtk.StateType.Active, dark_ui_panel);
                Gtk.Label chart_time_label = (Gtk.Label)Chart_Time_Toggle.Children[0];
                chart_time_label.ModifyFg(Gtk.StateType.Normal, dark_ui_foreground);
				chart_time_label.ModifyFg(Gtk.StateType.Prelight, new Gdk.Color(178, 178, 178));
				chart_time_label.ModifyFg(Gtk.StateType.Active, new Gdk.Color(178, 178, 178));
                //Selling_View_Scroller
			}

			current_ui_look = look;
            Save_UI_Config();
		}

		private void Save_UI_Config()
        {
            //Saves the UI information to a file
            try
            {
                using (System.IO.StreamWriter file =
                    new System.IO.StreamWriter(@App.App_Path + "/data/ui.ini", false))
                {
                    file.WriteLine("Version = 1");
                    string look = "";
                    if (current_ui_look == 0)
                    {
                        look = "Default";
                    }
                    else if (current_ui_look == 1)
                    {
                        look = "Light";
                    }
                    else if (current_ui_look == 2)
                    {
                        look = "Dark";
                    }
                    file.WriteLine("Look = " + look);
                    file.WriteLine("Default_Market = " + App.exchange_market);
                }
            }
            catch (Exception)
            {
                App.NebliDexNetLog("Failed to save user interface data to file");
            }
        }
        
        public static void Load_UI_Config()
        {
            if (File.Exists(App.App_Path + "/data/ui.ini") == false)
            {
                return; //Use the default themes as no UI file exists
            }
            int version = 0;
            try
            {
                using (System.IO.StreamReader file =
                    new System.IO.StreamReader(@App.App_Path + "/data/ui.ini", false))
                {
                    while (!file.EndOfStream)
                    {
                        string line_data = file.ReadLine();
                        line_data = line_data.ToLower();
                        if (line_data.IndexOf("=",StringComparison.InvariantCulture) > -1)
                        {
                            string[] variables = line_data.Split('=');
                            string key = variables[0].Trim();
                            string data = variables[1].Trim();
                            if (key == "version")
                            {
                                version = Convert.ToInt32(data);
                            }
                            else if (key == "look")
                            {
                                if (data == "default")
                                {
                                    App.default_ui_look = 0;
                                }
                                else if (data == "light")
                                {
                                    App.default_ui_look = 1;
                                }
                                else if (data == "dark")
                                {
                                    App.default_ui_look = 2;
                                }
                            }
                            else if (key == "default_market")
                            {
                                App.exchange_market = Convert.ToInt32(data);
                                if (App.exchange_market > App.total_markets) { App.exchange_market = App.total_markets; }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                App.NebliDexNetLog("Failed to read user interface data from file");
            }
        }     
        
	}
}
