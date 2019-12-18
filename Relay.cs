/*
 * Created by SharpDevelop.
 * User: David
 * Date: 4/7/2018
 * Time: 11:32 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

using System;
using Gtk;
using System.Data;
using NBitcoin;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;

//This code is designed to handle the Network for electrum servers and Critical Node infrastructure

namespace NebliDex_Linux
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App
	{
		
		//Used to determine if a message has already been received or not
        public static List<MessageRelay> MessageRelayList = new List<MessageRelay>();

        public class MessageRelay
        {
            public int utctime; //Time message was created
            public string msgnonce; //Nonce of message
        }

        public static bool SubmitMyOrder(OpenOrder ord, DexConnection dex)
        {
            //This function takes the order that we created and broadcast it to the connected critical node
            if (dex == null)
            {
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].contype == 3 && DexConnectionList[i].outgoing == true)
                        {
                            dex = DexConnectionList[i]; break; //Found our connection
                        }
                    }
                }
            }

            if (dex == null)
            {
				Application.Invoke(delegate
                {
                    MessageBox(null, "Notice", "Unable to connect to a Critical Node", "OK");
                });
                return false;
            }

            JObject js = new JObject();
            js["cn.method"] = "cn.sendorder";
            js["cn.response"] = 0;
            js["order.nonce"] = ord.order_nonce;
            js["order.market"] = ord.market;
            js["order.type"] = ord.type;
            js["order.price"] = ord.price.ToString(CultureInfo.InvariantCulture);
            js["order.originalamount"] = ord.original_amount.ToString(CultureInfo.InvariantCulture); //This will be the amount when broadcasted
            js["order.min_amount"] = ord.minimum_amount.ToString(CultureInfo.InvariantCulture);
            string json_encoded = JsonConvert.SerializeObject(js);
            string blockdata = "";
            lock (dex.blockhandle)
            {
                dex.blockhandle.Reset();
                SendCNServerAction(dex, 17, json_encoded); //Send to the CN and hopefully its not rejected
                if (dex.open == false) { return false; }
                dex.blockhandle.WaitOne(30000); //This will wait 30 seconds for a response                              
                if (dex.blockdata == "") { return false; }
                blockdata = dex.blockdata;
            }

            //The message will be displayed here
            if (blockdata != "Order OK")
            {
				//The order was rejected
				bool error_ok = CheckErrorMessage(blockdata);
                if (error_ok == false) { return false; } //Error message is not standard, don't show it
                Application.Invoke(delegate
                {
                    MessageBox(null, "Notice", blockdata, "OK");
                });
                return false;
            }

            return true; //Otherwise it is ok to submit our order and post it
        }

        public static bool SubmitMyOrderRequest(OpenOrder ord)
        {
            //This user has opted to create a market order instead of limit order
            //This function takes the order that we created and broadcast it to the connected critical node
            DexConnection dex = null;
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].contype == 3 && DexConnectionList[i].outgoing == true)
                    {
                        dex = DexConnectionList[i]; break; //Found our connection
                    }
                }
            }

            if (dex == null)
            {
				Application.Invoke(delegate
                {
                    MessageBox(null, "Notice", "Unable to connect to a Critical Node", "OK");
                });
                return false;
            }

            JObject js = new JObject();
            js["cn.method"] = "cn.sendorderrequest";
            js["cn.response"] = 0;
            js["order.nonce"] = ord.order_nonce;
            js["order.market"] = ord.market;
            js["order.type"] = ord.type;
            js["order.is_request"] = ord.is_request.ToString();
            js["order.price"] = ord.price.ToString(CultureInfo.InvariantCulture);
            js["order.originalamount"] = ord.original_amount.ToString(CultureInfo.InvariantCulture); //This will be the amount when broadcasted

            if (ord.type == 0)
            {
                //We are buying from seller, so my receive wallet is trade wallet
                js["taker.from_add"] = GetWalletAddress(MarketList[ord.market].base_wallet);
                js["taker.to_add"] = GetWalletAddress(MarketList[ord.market].trade_wallet);
            }
            else
            {
                //We are selling to buyer, so my receive wallet is base wallet
                js["taker.from_add"] = GetWalletAddress(MarketList[ord.market].trade_wallet);
                js["taker.to_add"] = GetWalletAddress(MarketList[ord.market].base_wallet);
            }

            string json_encoded = JsonConvert.SerializeObject(js);
            string blockdata = "";
            lock (dex.blockhandle)
            {
                dex.blockhandle.Reset();
                SendCNServerAction(dex, 17, json_encoded); //Send to the CN and hopefully its not rejected
                if (dex.open == false) { return false; }
                dex.blockhandle.WaitOne(30000); //This will wait 30 seconds for a response                              
                if (dex.blockdata == "") { return false; }
                blockdata = dex.blockdata;
            }

            //The message will be displayed here
            if (blockdata != "Order Request OK")
            {
				//The order was rejected
                bool error_ok = CheckErrorMessage(blockdata);
                if (error_ok == false) { return false; } //Error message is not standard, don't show
                Application.Invoke(delegate
                {
                    MessageBox(null, "Notice", blockdata, "OK");
                });
                return false;
            }

            return true; //Otherwise it is ok to post the request
        }

        public static bool CancelMarketOrder(JObject jord, bool checkip)
        {
            //This will attempt to find the order and remove it
            OpenOrder ord = new OpenOrder();
            ord.order_nonce = jord["order.nonce"].ToString();
            ord.type = Convert.ToInt32(jord["order.type"].ToString());
            if (ord.type != 0 && ord.type != 1) { return false; }
            ord.market = Convert.ToInt32(jord["order.market"].ToString());
            ord.is_request = Convert.ToBoolean(jord["order.is_request"].ToString());
			if (ord.market < 0 || ord.market >= total_markets) { return false; } //Unsupported data

            string ip = "";
            string port = "";
            string cn_ip = "";

            if (checkip == true)
            {
                ip = jord["order.ip"].ToString();
                port = jord["order.port"].ToString();
                cn_ip = jord["order.cn_ip"].ToString();
            }

            if (ord.is_request == true) { return false; } //Cannot remove order request this way

            bool order_exist = false;
            lock (OpenOrderList[ord.market])
            {
                for (int i = OpenOrderList[ord.market].Count - 1; i >= 0; i--)
                {
                    if (OpenOrderList[ord.market][i].order_nonce.Equals(ord.order_nonce) == true)
                    {
                        //Found order
                        if (checkip == true)
                        {
                            //CNs needs to validate that the cancellation request was made by the person who created it
                            if (OpenOrderList[ord.market][i].ip_address_port[0].Equals(ip) == true && OpenOrderList[ord.market][i].ip_address_port[1].Equals(port) == true && OpenOrderList[ord.market][i].cn_relayer_ip.Equals(cn_ip) == true)
                            {
                                OpenOrderList[ord.market].RemoveAt(i);
                                order_exist = true;
                            }
                            else
                            {
                                return false; //Someone elses order
                            }
                        }
                        else
                        {
                            OpenOrderList[ord.market].RemoveAt(i); //Assume the CN did its job
                            order_exist = true;
                        }
                    }
                }
            }

            //If the order doesn't exist yet, it may be on the way, store as cancellation token for when it gets here
            if (order_exist == false)
            {
                lock (CancelOrderTokenList)
                {
                    CancelOrderToken tk = new CancelOrderToken();
                    tk.arrivetime = UTCTime(); //Will delete in 5 minutes if no order arrives
                    tk.order_nonce = ord.order_nonce;
                    CancelOrderTokenList.Add(tk);
                }
            }
            else
            {
                //Now remove from view
                if (main_window_loaded == true)
                {
                    main_window.RemoveOrderFromView(ord);
                }
            }

            //Also remove the order request that this order was linked to
            lock (MyOpenOrderList)
            {
                for (int i = MyOpenOrderList.Count - 1; i >= 0; i--)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(ord.order_nonce) == true && MyOpenOrderList[i].queued_order == false)
                    {

						if (main_window_loaded == true)
                        {
                            if (MyOpenOrderList[i].is_request == true)
                            {
                                //let the user know that the other user closed the order
                                main_window.showTradeMessage("Trade Failed:\nOrder was cancelled by the creator or the creator is offline!");
                            }
                        }

                        if (MyOpenOrderList[i].is_request == true)
                        {
                            OpenOrder myord = MyOpenOrderList[i];
                            MyOpenOrderList.RemoveAt(i);
                            //Update the view
                            Application.Invoke(delegate
                            {
                                if (main_window_loaded == true)
                                {
                                    //Remove the row
                                    main_window.Open_Order_List_Public.NodeStore.RemoveNode(myord);
                                }
                            });
                        }
                        else
                        {
                            QueueMyOrderNoLock(MyOpenOrderList[i], null);
                        }

                    }
                }
            }

            //Close any order requests linked to this order too
            if (checkip == true)
            {
                //CN only function
                OrderRequest req = null;
                lock (OrderRequestList)
                {
                    for (int i = 0; i < OrderRequestList.Count; i++)
                    {
                        if (OrderRequestList[i].order_nonce_ref.Equals(ord.order_nonce) == true && OrderRequestList[i].order_stage < 3)
                        {
                            OrderRequestList[i].order_stage = 3; //Try to close the order request
                            req = OrderRequestList[i];
                            break;
                        }
                    }
                }

                //Now check if we are the validator
                if (req != null)
                {
                    AlertTradersIfValidator(req);
                }
            }

            //Successfully removed order
            return true;
        }

        public static void CNRequestCancelOrderNonce(int market, string order_nonce)
        {
            lock (OpenOrderList[market])
            {
                for (int pos = OpenOrderList[market].Count - 1; pos >= 0; pos--)
                {
                    if (OpenOrderList[market][pos].order_nonce == order_nonce)
                    {
                        OpenOrderList[market][pos].deletequeue = true; //Set order to be deleted
                        break;
                    }
                }
            }
        }

        public static void CNCancelOrder(OpenOrder ord)
        {
            //This is a CN function to cancel an order for a user

            if (main_window_loaded == true)
            {
                main_window.RemoveOrderFromView(ord);
            }

            JObject js = new JObject();
            js["cn.method"] = "cn.relaycancelorder";
            js["cn.response"] = 0;
            js["order.nonce"] = ord.order_nonce;
            js["order.market"] = ord.market;
            js["order.type"] = ord.type;
            js["order.ip"] = ord.ip_address_port[0];
            js["order.port"] = ord.ip_address_port[1];
            js["order.cn_ip"] = ord.cn_relayer_ip;
            js["order.is_request"] = ord.is_request;
            js["relay.nonce"] = GenerateHexNonce(24);
            js["relay.timestamp"] = UTCTime();
            CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));

            //Close any order requests for this order too
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_nonce_ref.Equals(ord.order_nonce) == true && OrderRequestList[i].order_stage < 3)
                    {
                        OrderRequestList[i].order_stage = 3; //Try to close the order request
                        break;
                    }
                }
            }

            try
            {
                RelayCancelOrder(null, js); //Broadcast this to all available nodes
            }
            catch (Exception e)
            {
                NebliDexNetLog("CN Failed to broadcast cancellation request, error: " + e.ToString());
            }

            return;

        }

        public static void CloseFailedOrderRequest(OrderRequest req)
        {
            //This function will close an Order request linked to this CN and broadcast the closure of the order
            string target_ip = req.ip_address_taker[0];
            string target_port = req.ip_address_taker[1];
            string target_cn = req.taker_cn_ip;
            lock (DexConnectionList)
            {
                for (int i2 = 0; i2 < DexConnectionList.Count; i2++)
                {
                    if (DexConnectionList[i2].outgoing == false && DexConnectionList[i2].contype == 3 && DexConnectionList[i2].version >= protocol_min_version)
                    {
                        //Linked TN
                        if (DexConnectionList[i2].ip_address[0].Equals(target_ip) == true && DexConnectionList[i2].ip_address[1].Equals(target_port) == true && target_cn == getPublicFacingIP())
                        {
                            //This makes me responsible for closing the order as well

                            //Then close order linked to failed request
                            lock (OpenOrderList[req.market])
                            {
                                for (int pos = 0; pos < OpenOrderList[req.market].Count; pos++)
                                {
                                    if (OpenOrderList[req.market][pos].order_nonce.Equals(req.order_nonce_ref) == true)
                                    {
                                        //Mark the order for deletion
                                        OpenOrderList[req.market][pos].deletequeue = true;
                                        break;
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }

            AlertTradersIfValidator(req);
        }

        public static bool AlertTradersIfValidator(OrderRequest req)
        {
            //This function will alert the maker if the order request failed
            string my_pubkey = GetWalletPubkey(3);
            if (req.validator_pubkey.Equals(my_pubkey) == true)
            {
                //This is our order request
                //Can only cancel trade right before we broadcast taker transaction information
                string trade_status_string = GetValidationData("status", req.utctime, req.order_nonce_ref);
                if (trade_status_string.Length > 0)
                {
                    int trade_status = Convert.ToInt32(trade_status_string);
                    if (trade_status == 0)
                    {
                        bool ok = SetValidationData("status", 4, req.utctime, req.order_nonce_ref); //Cancel
                        if (ok == true)
                        {
                            //Cancel this trade and alert maker of failed fund
                            SetValidationData("taker_tx", "", req.utctime, req.order_nonce_ref);
                            SetValidationData("taker_feetx", "", req.utctime, req.order_nonce_ref);
                            SetValidationData("claimed", 1, req.utctime, req.order_nonce_ref); //Nothing to claim
                            SendPostTradeAction(1, req.utctime, req.order_nonce_ref); //Tell maker trade is canceled
                            SendPostTradeAction(0, req.utctime, req.order_nonce_ref); //Tell taker trade is canceled
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public static void QueueAllOpenOrders()
        {
            //Queues all the open orders
            App.DexConnection dex = null;
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].contype == 3 && DexConnectionList[i].outgoing == true && DexConnectionList[i].open == true)
                    {
                        dex = DexConnectionList[i]; break; //Found our connection
                    }
                }
            }

            //Queue all the open orders not already queued
            lock (MyOpenOrderList)
            {
                for (int i = MyOpenOrderList.Count - 1; i >= 0; i--)
                {
                    if (MyOpenOrderList[i].queued_order == false)
                    {
                        QueueMyOrderNoLock(MyOpenOrderList[i], dex);
                    }
                }
            }
        }

        public static void QueueAllButOpenOrders(OpenOrder ord)
        {
            //Queues all but certain open orders
            App.DexConnection dex = null;
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].contype == 3 && DexConnectionList[i].outgoing == true && DexConnectionList[i].open == true)
                    {
                        dex = DexConnectionList[i]; break; //Found our connection
                    }
                }
            }

            //Queue all the open orders not already queued that don't match our open order
            lock (MyOpenOrderList)
            {
                for (int i = MyOpenOrderList.Count - 1; i >= 0; i--)
                {
                    if (MyOpenOrderList[i].queued_order == false && MyOpenOrderList[i].order_nonce.Equals(ord.order_nonce) == false)
                    {
                        QueueMyOrderNoLock(MyOpenOrderList[i], dex);
                    }
                }
            }
        }

        public static void QueueMyOrderNoLock(OpenOrder ord, DexConnection dex)
        {
            //This function will queue an open order
            bool found = false;
            for (int i = 0; i < MyOpenOrderList.Count; i++)
            {
                if (MyOpenOrderList[i].order_nonce.Equals(ord.order_nonce) == true && MyOpenOrderList[i].is_request == false)
                {
                    ord.queued_order = true; //Set queue status to true
                    ord.order_stage = 0;
                    ord.pendtime = UTCTime();
                    found = true;
                    break;
                }
            }

            if (found == false) { return; }

            //Now remove the order from my order book
            if (ord.is_request == false)
            {
                lock (OpenOrderList[ord.market])
                {
                    for (int i = OpenOrderList[ord.market].Count - 1; i >= 0; i--)
                    {
                        //Remove any order that matches our nonce
                        if (OpenOrderList[ord.market][i].order_nonce.Equals(ord.order_nonce) == true)
                        {
                            OpenOrderList[ord.market].RemoveAt(i);
                        }
                    }
                }
                
				//Now remove the order from view
                if (main_window_loaded == true)
                {
                    main_window.RemoveOrderFromView(ord);
                }
            }

			//Force redraw the view
            if (main_window_loaded == true)
            {
                Application.Invoke(delegate
                {
                    main_window.Open_Order_List_Public.QueueDraw();
                });
            }

            if (dex == null) { return; }

            //Next broadcast to CN to cancel the order
            JObject js = new JObject();
            js["cn.method"] = "cn.cancelorder";
            js["cn.response"] = 0;
            js["order.nonce"] = ord.order_nonce;
            js["order.market"] = ord.market;
            js["order.type"] = ord.type;
            js["order.is_request"] = ord.is_request.ToString();

            string json_encoded = JsonConvert.SerializeObject(js);
            try
            {
                SendCNServerAction(dex, 22, json_encoded); //Send cancel request, no need to wait
            }
            catch (Exception)
            {
                NebliDexNetLog("Failed to broadcast cancellation request");
            }

            //Now the order is queued and will attempt to rebroadcast every 30 seconds
        }

        public static void CancelMyOrder(OpenOrder ord)
        {
            //This function takes the order that we created, cancel it then broadcast the request to the connected critical node

            //First things first, cancel the order on my side (so even if server is down, order is still cancelled)
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(ord.order_nonce) == true)
                    {
						OpenOrder myord = MyOpenOrderList[i];
                        if (main_window_loaded == true)
                        {
                            Application.Invoke(delegate
                            {
                                main_window.Open_Order_List_Public.NodeStore.RemoveNode(myord);
                            });
                        }
                        RemoveSavedOrder(ord); //Take the order out of the saved table
                        MyOpenOrderList.RemoveAt(i);
                        break; //Take it out
                    }
                }
            }

            if (ord.queued_order == true)
            { //Order is already not posted anyway
                return;
            }

            //Next take it out of the open orders
            if (ord.is_request == false)
            {
                lock (OpenOrderList[ord.market])
                {
                    for (int i = OpenOrderList[ord.market].Count - 1; i >= 0; i--)
                    {
                        //Remove any order that matches our nonce
                        if (OpenOrderList[ord.market][i].order_nonce.Equals(ord.order_nonce) == true)
                        {
                            OpenOrderList[ord.market].RemoveAt(i);
                        }
                    }
                }

                //Now remove the order from view
                if (main_window_loaded == true)
                {
                    main_window.RemoveOrderFromView(ord);
                }
            }

            //Now get an CN and send the request
            DexConnection dex = null;
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].contype == 3 && DexConnectionList[i].outgoing == true)
                    {
                        dex = DexConnectionList[i]; break; //Found our connection
                    }
                }
            }

            if (dex == null) { return; }

            JObject js = new JObject();
            js["cn.method"] = "cn.cancelorder";
            js["cn.response"] = 0;
            js["order.nonce"] = ord.order_nonce;
            js["order.market"] = ord.market;
            js["order.type"] = ord.type;
            js["order.is_request"] = ord.is_request.ToString();

            string json_encoded = JsonConvert.SerializeObject(js);
            try
            {
                SendCNServerAction(dex, 22, json_encoded); //Send cancel request, no need to wait
            }
            catch (Exception)
            {
                NebliDexNetLog("Failed to broadcast cancellation request");
            }

            return; //Otherwise it is ok to cancel our order
        }

        public static JObject NewOrderRequestJSON(OrderRequest req)
        {
            //Convert giant object to JSON
            JObject js = new JObject();
            js["cn.method"] = "cn.relayorderrequest";
            js["cn.response"] = 0;
            js["cn.authlevel"] = 1;
            js["request.order_nonce"] = req.order_nonce_ref;
            js["request.market"] = req.market;
            js["request.time"] = req.utctime;
            js["request.type"] = req.type;
            js["request.stage"] = req.order_stage;
            js["request.fee"] = req.ndex_fee.ToString(CultureInfo.InvariantCulture);
            js["request.taker_from_add"] = req.from_add_1;
            js["request.taker_to_add"] = req.to_add_2;
            js["request.amount_1"] = req.amount_1.ToString(CultureInfo.InvariantCulture);
            js["request.amount_2"] = req.amount_2.ToString(CultureInfo.InvariantCulture);
            js["request.maker_ip"] = req.ip_address_maker[0];
            js["request.maker_port"] = req.ip_address_maker[1];
            js["request.taker_ip"] = req.ip_address_taker[0];
            js["request.taker_port"] = req.ip_address_taker[1];
            js["request.maker_cn_ip"] = req.maker_cn_ip;
            js["request.taker_cn_ip"] = req.taker_cn_ip;
            js["request.request_id"] = req.request_id;
            js["relay.nonce"] = GenerateHexNonce(24);
            js["relay.timestamp"] = UTCTime();
            return js;
        }

        //Relay Network Stuff
        public static void RelayShowOrder(DexConnection fromcon, JObject js)
        {
            if (js["relay.nonce"] == null)
            {
                js["relay.nonce"] = GenerateHexNonce(24);
                js["relay.timestamp"] = UTCTime();
                CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
            }

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //We will relay the close order request information. This occurs when the taker has received the redeem script
            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This connection is a CN, send a new CN message to it
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                        SendCNServerAction(DexConnectionList[i], 46, JsonConvert.SerializeObject(js));
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("CN Receive Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node Show Order Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }

        }

        public static void RelayCloseOrderRequest(DexConnection fromcon, JObject js)
        {

            //Get the Order Request Info, and modify it
            OrderRequest req = null;
            JObject tradeinfo = null;
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_nonce_ref.Equals(js["cn.order_nonce"].ToString()) == true && OrderRequestList[i].order_stage < 3)
                    {
                        OrderRequestList[i].order_stage = 3; //Order was closed
                        req = OrderRequestList[i];
                        if (Convert.ToInt32(js["trade.success"].ToString()) == 1)
                        {
                            decimal trade_amount = 0;
                            decimal trade_price = 0;
                            int trade_time = UTCTime();
                            if (req.type == 0)
                            {
                                trade_amount = req.amount_2;
                                trade_price = req.amount_1 / req.amount_2;
                            }
                            else
                            {
                                trade_amount = req.amount_1;
                                trade_price = req.amount_2 / req.amount_1;
                            }
                            tradeinfo = new JObject(); //Transmit the trade information to all the connected nodes
                            tradeinfo["cn.method"] = "cn.trade_complete";
                            tradeinfo["cn.response"] = 0;
                            tradeinfo["trade.price"] = trade_price.ToString(CultureInfo.InvariantCulture);
                            tradeinfo["trade.amount"] = trade_amount.ToString(CultureInfo.InvariantCulture);
                            tradeinfo["trade.market"] = req.market;
                            tradeinfo["trade.type"] = req.type; //We will use the order requestors type on the recent trades
                            tradeinfo["trade.order_nonce"] = req.order_nonce_ref; //So we can deduct it from the openorder
                            trade_time = Convert.ToInt32(js["trade.complete_time"].ToString());
                            tradeinfo["trade.complete_time"] = js["trade.complete_time"]; //To assure synchronous operation                     
                            ExchangeWindow.AddRecentTradeToView(req.market, req.type, trade_price, trade_amount, req.order_nonce_ref, trade_time);
                        }
                        break;
                    }
                }
            }
            if (req == null) { return; }

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //We will relay the close order request information. This occurs when the taker has received the redeem script
            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This connection is a CN, send a new CN message to it
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                        if (tradeinfo != null)
                        {
                            //Let all the clients know this trade is complete
                            SendCNServerAction(DexConnectionList[i], 46, JsonConvert.SerializeObject(tradeinfo));
                        }
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("CN Receive Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node Close Order Request Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }

        }

        public static void RelayOrderRequest(DexConnection fromcon, JObject js)
        {
            //We will rebroadcast this object to all the CNs indirectly
            //And find the TN that this message was directed to

            string target_ip = js["request.maker_ip"].ToString();
            string target_port = js["request.maker_port"].ToString();
            int portcombo = Convert.ToInt32(js["request.taker_port"].ToString()) + Convert.ToInt32(js["request.maker_port"].ToString());
            int who_validate = portcombo % 2;

            //Get the CNs IP for the maker and taker
            string target_maker_cn = js["request.maker_cn_ip"].ToString();
            string target_taker_cn = js["request.taker_cn_ip"].ToString();

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This connection is a CN, send a new CN message to it
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }

                        //Send message to linked TNs to temporarily hide the order connected to the request
                        SendCNServerAction(DexConnectionList[i], 39, js["request.order_nonce"].ToString());
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("CN Acceptance Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();

                    }
                    client.Close();
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node Relay Order Request Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }

            //Now go through our list again and find our order creating TN that may be linked to this request
            //This has to be done after broadcasting message to other CNs
            if (target_maker_cn.Equals(my_ip) == true)
            { //We have to be the CN that initially broadcasted this order
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version)
                        {
                            //Linked TN
                            if (DexConnectionList[i].ip_address[0].Equals(target_ip) == true && DexConnectionList[i].ip_address[1].Equals(target_port) == true)
                            {
                                //Found the TN linked to this order request
                                JObject tjs = new JObject();
                                tjs["cn.method"] = "cn.tradeavail";
                                tjs["cn.response"] = 0;
                                tjs["cn.order_nonce"] = js["request.order_nonce"].ToString();
                                int ttype = Convert.ToInt32(js["request.type"].ToString());
                                if (ttype == 0)
                                {
                                    //Taker is buying
                                    tjs["cn.amount"] = js["request.amount_2"]; //This is the trade amount requested by taker
                                }
                                else
                                {
                                    //Taker is selling
                                    tjs["cn.amount"] = js["request.amount_1"]; //This is the trade amount taker is sending to maker
                                }
                                tjs["cn.who_validate"] = who_validate; //If this value is 0, then the maker will validate
                                SendCNServerAction(DexConnectionList[i], 40, JsonConvert.SerializeObject(tjs)); //Send TN that request has been asked
                                break;
                            }
                        }
                    }
                }
            }
        }

        public static void RelayValidatorInfo(DexConnection fromcon, JObject js, bool totaker)
        {
            if (js["relay.nonce"] == null)
            {
                //No message relay yet for this message
                js["relay.nonce"] = GenerateHexNonce(24);
                js["relay.timestamp"] = UTCTime();
                CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
            }

            string target_ip = "";
            string target_port = "";
            string target_cn = "";

            //Get the Order Request Info, and modify it
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_nonce_ref.Equals(js["cn.order_nonce"].ToString()) == true && OrderRequestList[i].order_stage < 3)
                    {
                        if (totaker == false)
                        {
                            target_ip = OrderRequestList[i].ip_address_maker[0];
                            target_port = OrderRequestList[i].ip_address_maker[1];
                            target_cn = OrderRequestList[i].maker_cn_ip;
                        }
                        else
                        {
                            target_ip = OrderRequestList[i].ip_address_taker[0];
                            target_port = OrderRequestList[i].ip_address_taker[1];
                            target_cn = OrderRequestList[i].taker_cn_ip;
                        }

                        if (OrderRequestList[i].validator_pubkey == "")
                        {
                            OrderRequestList[i].validator_pubkey = js["cn.validator_pubkey"].ToString(); //Everyone knows who the validator is
                            OrderRequestList[i].order_stage = 2; //Validator has been selected
                        }

                        if (OrderRequestList[i].validator_pubkey.Equals(js["cn.validator_pubkey"].ToString()) == false)
                        {
                            NebliDexNetLog("Validator info doesn't match, stopping relay for: " + OrderRequestList[i].order_nonce_ref);
                            return;
                        }
                        //Validation doesn't start until received tx from taker                     
                        break;
                    }
                }
            }

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //We will rebroadcast this object to all the CNs indirectly
            //And find the TN that this message was directed to

            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This connection is a CN, send a new CN message to it
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("Validator Acceptance Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node Relay Validator Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }

            //Now go through our list again and find our TN that is the maker of this request
            //This has to be done after broadcasting message to other CNs
            if (target_cn.Equals(my_ip) == true)
            {
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version)
                        {
                            //Linked TN
                            if (DexConnectionList[i].ip_address[0].Equals(target_ip) == true && DexConnectionList[i].ip_address[1].Equals(target_port) == true)
                            {
                                //Found the TN linked to this order request
                                SendCNServerAction(DexConnectionList[i], 40, JsonConvert.SerializeObject(js)); //Send TN that request has been asked
                                break;
                            }
                        }
                    }
                }
            }
        }

        public static void RelayTradeMessage(DexConnection fromcon, JObject js)
        {
            //We will rebroadcast this object to all the CNs indirectly
            //And find the TN that this message was directed to

            int request_status = 0;

            string target_ip = "";
            string target_port = "";
            string target_cn = "";
            int who_validate = 0;
            int market = 0;
            string method = js["cn.method"].ToString();
            string result = js["cn.result"].ToString();

            if (method == "cn.relaytradeavail")
            {
                //From maker
                if (result == "Trade Rejected")
                {
                    request_status = 3; //Close the request
                }
                else if (result == "Trade Accepted")
                {
                    request_status = 1; //The order request has been recognized by the TN
                }

                lock (OrderRequestList)
                {
                    for (int i = 0; i < OrderRequestList.Count; i++)
                    {
                        if (OrderRequestList[i].order_nonce_ref.Equals(js["cn.order_nonce"].ToString()) == true && OrderRequestList[i].order_stage < 3)
                        {
                            //Update the maker addresses
                            OrderRequestList[i].to_add_1 = js["trade.maker_receive_add"].ToString();
                            OrderRequestList[i].from_add_2 = js["trade.maker_send_add"].ToString();

                            bool valid_orderrequest = true;

                            if (js["trade.request_id"] == null && OrderRequestList[i].request_id.Length > 0)
                            {
                                js["trade.request_id"] = OrderRequestList[i].request_id;
                            }
                            else if (js["trade.request_id"] != null)
                            {
                                string request_id = js["trade.request_id"].ToString();
                                if (request_id.Equals(OrderRequestList[i].request_id) == false)
                                {
                                    //This is a different request for the same order, close it
                                    valid_orderrequest = false;
                                }
                            }

                            if (valid_orderrequest == true)
                            {
                                target_ip = OrderRequestList[i].ip_address_taker[0];
                                target_port = OrderRequestList[i].ip_address_taker[1];
                                target_cn = OrderRequestList[i].taker_cn_ip;
                                who_validate = OrderRequestList[i].who_validate;
                                market = OrderRequestList[i].market;
                                OrderRequestList[i].order_stage = request_status;
                            }
                            else
                            {
                                OrderRequestList[i].order_stage = 3; //Close this request as it wasn't the first to arrive to maker
                            }
                        }
                    }
                }
            }

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This connection is a CN, send a new CN message to it
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("Trade Relay status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node Trade Message Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }

            bool cancel_order = false;
            //Now go through our list again and find our TN that may be linked to this request
            //This has to be done after broadcasting message to other CNs
            if (target_cn.Equals(my_ip) == true)
            { //This TN is one of my TNs
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version)
                        {
                            //Linked TN
                            if (DexConnectionList[i].ip_address[0].Equals(target_ip) == true && DexConnectionList[i].ip_address[1].Equals(target_port) == true)
                            {
                                //Found the TN linked to this order request

                                if (method == "cn.relaytradeavail")
                                {
                                    //Response from the TN maker

                                    JObject tjs = new JObject();
                                    tjs["cn.method"] = "cn.trademessage";
                                    tjs["cn.response"] = 0;
                                    tjs["cn.order_nonce"] = js["cn.order_nonce"].ToString();

                                    if (result == "Trade Accepted")
                                    {
                                        tjs["cn.who_validate"] = who_validate; //If this value is 1, then the taker will validate
                                    }
                                    else
                                    {
                                        //We will cancel the order because your trade shouldn't be rejected this way
                                        cancel_order = true;
                                        tjs["cn.result"] = "Request Failed: Your trade request has been rejected";
                                    }
                                    SendCNServerAction(DexConnectionList[i], 40, JsonConvert.SerializeObject(tjs)); //Send TN that request has been asked
                                }

                                break;
                            }
                        }
                    }
                }
            }

            if (cancel_order == true)
            {
                lock (OpenOrderList[market])
                {
                    for (int i = 0; i < OpenOrderList[market].Count; i++)
                    {
                        if (OpenOrderList[market][i].order_nonce.Equals(js["cn.order_nonce"].ToString()) == true)
                        {
                            OpenOrderList[market][i].deletequeue = true; //Mark order for deletion
                        }
                    }
                }
            }

        }

        public static void RelayCancelOrder(DexConnection fromcon, JObject js)
        {
            JObject jord = new JObject();
            jord["cn.method"] = "cn.relaycancelorder";
            jord["cn.response"] = 0;
            jord["order.nonce"] = js["order.nonce"];
            jord["order.market"] = js["order.market"];
            jord["order.type"] = js["order.type"];
            jord["order.is_request"] = js["order.is_request"];

            bool is_request = Convert.ToBoolean(js["order.is_request"].ToString());

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //Broadcast this order to all the nodes on the dex, indirectly and directly
            //First send order to connected TNs
            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        //Also make sure to avoid sending the message to the connection that sent it to us
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This IP address is linked to a CN, send an additional data packet with IP, nonce and timestamp
                            //CNs do not recognize the neworder for TNs
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                        //Send the message to the TN/CN (not recognized by CN)
                        //It is also possible this IP is linked to someone else on a CN network other than CN
                        if (is_request == false)
                        { //Only relay regular Orders to TNs
                            SendCNServerAction(DexConnectionList[i], 23, JsonConvert.SerializeObject(jord));
                        }
                    }
                }
            }

            //Now add the IP addresses & port from the json
            jord["order.ip"] = js["order.ip"];
            jord["order.port"] = js["order.port"];
            jord["order.cn_ip"] = js["order.cn_ip"];
            jord["relay.nonce"] = js["relay.nonce"]; //Used for relay messages
            jord["relay.timestamp"] = Convert.ToInt32(js["relay.timestamp"].ToString());

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            //Connect to them and send them the encoded order
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(jord); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("Cancel Order Relay Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node Cancel Order Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }
        }

        public static void RelayNewCN(DexConnection fromcon, JObject js)
        {

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //We will rebroadcast this object to all the CNs indirectly

            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This connection is a CN, send a new CN message to it
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false && cn_ip.Equals(fromcon.ip_address[0]) == false)
                    { //Do not want same IP address or new CN IP
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("CN Acceptance Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node New CN Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }
        }

        public static void RelayNewOrder(DexConnection fromcon, JObject js)
        {
            //We've checked this order so relay this order
            JObject jord = new JObject();
            jord["cn.method"] = js["cn.method"];
            jord["cn.response"] = js["cn.response"];
            jord["order.nonce"] = js["order.nonce"];
            jord["order.market"] = js["order.market"];
            jord["order.type"] = js["order.type"];
            jord["order.price"] = js["order.price"];
            jord["order.originalamount"] = js["order.originalamount"];
            jord["order.min_amount"] = js["order.min_amount"];

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //Broadcast this order to all the nodes on the dex, indirectly and directly
            //First send order to connected TNs
            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit message to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This IP address is linked to a CN, send an additional data packet with IP, nonce and timestamp
                            //CNs do not recognize the neworder for TNs
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }
                        //Send the message to the TN/CN (not recognized by CN)
                        //It is also possible this IP is linked to someone else on a CN network other than CN
                        SendCNServerAction(DexConnectionList[i], 20, JsonConvert.SerializeObject(jord));
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now go through the list of CNs
            //Connect to them and send them the encoded order
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(js); //The full order information
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("Order Relay Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node New Order Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }
        }

        public static void BroadcastNewOrder(DexConnection fromcon, OpenOrder ord)
        {
            //Create Jobject from order
            JObject jord = new JObject();
            jord["cn.method"] = "cn.neworder";
            jord["cn.response"] = 0;
            jord["order.nonce"] = ord.order_nonce;
            jord["order.market"] = ord.market;
            jord["order.type"] = ord.type;
            jord["order.price"] = ord.price.ToString(CultureInfo.InvariantCulture);
            jord["order.originalamount"] = ord.original_amount.ToString(CultureInfo.InvariantCulture);
            jord["order.min_amount"] = ord.minimum_amount.ToString(CultureInfo.InvariantCulture);

            string relay_nonce = GenerateHexNonce(24);
            int relay_time = UTCTime();

            //Add this relay potential message to our relay queue so if we get it again, we do not rebroadcast
            CheckMessageRelay(relay_nonce, relay_time);

            //Get my external IP address
            string my_ip = getPublicFacingIP();

            //Broadcast this open order to all the nodes on the dex, indirectly and directly
            //First send order to connected TNs
            List<string> CN_outbound = new List<string>(); //Our list of CNs to transmit order to
            lock (DexConnectionList)
            {
                for (int i = 0; i < DexConnectionList.Count; i++)
                {
                    if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 3 && DexConnectionList[i].version >= protocol_min_version && DexConnectionList[i] != fromcon)
                    {
                        //Linked TNs and CNs
                        if (CheckIfCN(DexConnectionList[i]) == true)
                        {
                            //This IP address is linked to a CN, send an additional data packet with IP, nonce and timestamp
                            //CNs do not recognize the neworder for TNs
                            if (DexConnectionList[i].ip_address[0].Equals(my_ip) == false)
                            {
                                CN_outbound.Add(DexConnectionList[i].ip_address[0]);
                            }
                        }

                        //Send the message to the TN/CN (not recognized by CN)
                        //It is also possible this IP is linked to someone else on a CN network other than CN
                        //It is also possible a CN may receive this information as well
                        SendCNServerAction(DexConnectionList[i], 20, JsonConvert.SerializeObject(jord));
                    }
                }
            }

            //All CNs will get message because every CN is connected to at least one other CN at all times
            //Instead of sending a message to all CNs, send a message to all connected CNs and if
            //that is less than 5, upto 5 additional random CNs
            List<string> cn_ips = new List<string>();
            lock (CN_Nodes_By_IP)
            {
                foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                {
                    if (cn_ip.Equals(my_ip) == false)
                    { //Do not want same IP address
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                //Do this for at most 5 CN
                if (CN_outbound.Count >= 5) { break; } //We have enough outbound CNs
                if (cn_ips.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                CN_outbound.Add(cn_ips[pos]);
                cn_ips.RemoveAt(pos);
            }

            //Now add additional information for other CNs
            jord["cn.authlevel"] = 1; //This is a critical node to critical node feature (low level auth)
            jord["order.ip"] = ord.ip_address_port[0];
            jord["order.port"] = ord.ip_address_port[1];
            jord["order.cn_ip"] = ord.cn_relayer_ip;
            jord["relay.nonce"] = relay_nonce; //Used for relay messages
            jord["relay.timestamp"] = relay_time;

            //Now go through the list of CNs
            //Connect to them and send them the encoded order
            for (int i = 0; i < CN_outbound.Count; i++)
            {
                try
                {
                    IPAddress address = IPAddress.Parse(CN_outbound[i]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("CN Node: Connected to: " + CN_outbound[i]);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        string json_encoded = JsonConvert.SerializeObject(jord);
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        NebliDexNetLog("Order Relay Status: " + response); //Hopefully order received

                        if (response.Length > 0)
                        {
                            JObject myresp = JObject.Parse(response);
                            if (myresp["cn.result"].ToString() == "Not a CN" && PeriodQueryOpen == false)
                            {
                                //Try to reconnect the CN
                                reconnect_cn = true;
                            }
                        }

                        nStream.Close();
                        client.Close();

                    }
                    else
                    {
                        client.Close();
                        throw new Exception("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Critical Node New Order Relay Error: " + CN_outbound[i] + " error: " + e.ToString());
                }
            }
        }

        public static bool CheckMessageRelay(string nonce, int time)
        {
            //This will check the message relay for its existance, and add it if it doesn't exist
            lock (MessageRelayList)
            {
                for (int i = 0; i < MessageRelayList.Count; i++)
                {
                    if (MessageRelayList[i].msgnonce.Equals(nonce) == true && MessageRelayList[i].utctime == time)
                    {
                        return true; //The message exists already, disregard
                    }
                }
                MessageRelay rl = new MessageRelay();
                rl.msgnonce = nonce;
                rl.utctime = time;
                MessageRelayList.Add(rl);
                if (MessageRelayList.Count > 1000)
                {
                    //Too many things on the list
                    MessageRelayList.RemoveAt(0); //Remove the oldest relay
                }
                return false; //New message
            }
        }
		
	}
	
}