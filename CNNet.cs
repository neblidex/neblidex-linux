/*
 * Created by SharpDevelop.
 * User: David
 * Date: 4/13/2018
 * Time: 12:54 PM
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
using Mono.Data.Sqlite;
using System.Globalization;

//This code is designed to handle the Network for electrum servers and Critical Node infrastructure

namespace NebliDex_Linux
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App
	{

		//Critical Node Infrastructure
        public static TcpListener critical_node_server;
        public static bool reconnect_cn = false; //In case the cn connection abruptly ends
        public static bool cn_network_down = false; //This will be true if the internet goes down
        public static decimal my_cn_weight = 0; //The chance of validating next trade

        public static async void ToggleCriticalNodeServer(bool activate)
        {
            if (activate == false && critical_node_server != null)
            {
                //We need to disconnect the critical node
                critical_node = false;
                critical_node_server.Stop(); //Will cause an exception from running server
                NebliDexNetLog("Critical Node Server Stopped");
                critical_node_server = null;

                string my_ip = getPublicFacingIP();
                lock (CN_Nodes_By_IP)
                {
                    CN_Nodes_By_IP.Remove(my_ip);
                }

                //Drop any connections as well
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false)
                        {
                            DexConnectionList[i].open = false; //Close the connection
                        }
                        if (DexConnectionList[i].contype == 0)
                        {
                            DexConnectionList[i].open = false; //Close the BlockHelper connection as well
                        }
                    }
                }

                using_blockhelper = false;
                return;
            }

            //When this function is ran, it toggles the critical node
            if (activate == true)
            {
                try
                {
                    critical_node = true;
                    critical_node_server = new TcpListener(IPAddress.Any, critical_node_port);
                    critical_node_server.Start(); //Start to listen at this port
                    NebliDexNetLog("Critical Node Server Listening...");
                    while (critical_node == true)
                    {
                        //This status may change
                        try
                        {
                            TcpClient client = await critical_node_server.AcceptTcpClientAsync(); //This will wait for a client
                                                                                                  //The IP address of the person on the other side and port they used to connect to
                                                                                                  //A new client connected, create a dex connection
                            string ip_add = ((IPEndPoint)client.Client.RemoteEndPoint).Address.MapToIPv4().ToString();
                            string port = ((IPEndPoint)client.Client.RemoteEndPoint).Port.ToString();

                            NebliDexNetLog("Server: Connection Accepted: " + ip_add);
                            DexConnection dex = new DexConnection();
                            dex.ip_address[0] = ip_add;
                            dex.ip_address[1] = port;
                            dex.outgoing = false; //Received this connection
                            dex.open = true; //The connection is open
                            dex.contype = 3; //CN connection
                            dex.client = client;
                            dex.stream = client.GetStream();

                            //Check to see if a connection limit has been exceeded
                            bool max_connection = false;

                            if (TN_Connections.ContainsKey(ip_add) == true)
                            {
                                int con_num = TN_Connections[ip_add];
                                if (con_num > 100)
                                {
                                    max_connection = true;
                                }
                                else
                                {
                                    lock (TN_Connections)
                                    {
                                        TN_Connections[ip_add] = TN_Connections[ip_add] + 1;
                                    }
                                }
                            }
                            else
                            {
                                lock (TN_Connections)
                                {
                                    TN_Connections.Add(ip_add, 1); //New IP address
                                }
                            }

                            if (max_connection == false)
                            {
                                lock (DexConnectionList)
                                {
                                    DexConnectionList.Add(dex);
                                }
                                //Setup the callback that runs when data is received into connection
                                dex.stream.BeginRead(dex.buf, 0, 1, DexConnCallback, dex);
                            }
                            else
                            {
                                dex.closeConnection(); //Close connection and free up resources
                            }
                        }
                        catch (Exception e)
                        {
                            NebliDexNetLog("Failed to connect to a client, error: " + e.ToString());
                        }
                    }
                    NebliDexNetLog("Critical Node Server Stopped Listening...");
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Disconnected critical node server, error: " + e.ToString());
                    critical_node = false;
                }
            }
        }

        public static string getPublicFacingIP()
        {
            //This will return the IP address of the router/computer public facing
            //Will use this to add the Critical Node to CN lists
            string localip = "";

            if (my_external_ip.Length > 0) { return my_external_ip; } //Return our cached IP address

            //These modes of for development and testing
            if (wlan_mode == 2)
            {
                //Over localhost only
                localip = "127.0.0.1";
                my_external_ip = localip;
                return localip;
            }
            else if (wlan_mode == 1)
            {
				//Over Lan
                //Use a UDP Local IP For Now
                //This only works for behind network tests

                using (System.Net.Sockets.Socket socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    localip = endPoint.Address.ToString();
                }
                my_external_ip = localip;
                return localip;
            }


            if (DNS_SEED_TYPE == 0)
            { //HTTP IP lookup
                bool timeout;
                localip = HttpRequest(DNS_SEED + "/?api=my_remote_ip", "", out timeout);
            }

            if (localip == "")
            {
                //Still not able to find the IP address, query other CNs for my IP and use the one most prevalent

                List<string> my_ip = new List<string>();
                List<int> my_ip_prevalance = new List<int>();
                List<string> cn_ips = new List<string>();
                lock (CN_Nodes_By_IP)
                {
                    foreach (string cn_ip in CN_Nodes_By_IP.Keys)
                    {
                        cn_ips.Add(cn_ip); //Convert our dictionary to an array of strings
                    }
                }

                for (int i = 0; i < 10; i++)
                {
                    //Do this for at most 10 CN
                    if (cn_ips.Count == 0) { break; }
                    int pos = (int)Math.Round(GetRandomNumber(1, cn_ips.Count)) - 1;
                    try
                    {
                        IPAddress address = IPAddress.Parse(cn_ips[pos]);
                        int port = critical_node_port;
                        TcpClient client = new TcpClient();
                        //This will wait 5 seconds before moving on (bad connection)
                        if (ConnectSync(client, address, port, 5))
                        {
                            NebliDexNetLog("CN Node: Connected to: " + cn_ips[pos]);
                            //Connect to the critical node and get a list of possible critical nodes
                            client.ReceiveTimeout = 5000; //5 seconds
                            NetworkStream nStream = client.GetStream(); //Get the stream

                            JObject js = new JObject();
                            js["cn.method"] = "cn.reflectip";
                            js["cn.response"] = 0; //This is telling another CN that this is a request

                            string json_encoded = JsonConvert.SerializeObject(js);
                            string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                            //Convert it to a JSON
                            js = JObject.Parse(response);
                            string r_method = js["cn.method"].ToString();
                            if (r_method.Equals("cn.reflectip"))
                            {
                                string ip = js["cn.result"].ToString();
                                bool present = false;
                                for (int i2 = 0; i2 < my_ip.Count; i2++)
                                {
                                    if (my_ip[i2].Equals(ip) == true)
                                    {
                                        present = true;
                                        my_ip_prevalance[i2]++;
                                        break;
                                    }
                                }
                                if (present == false)
                                {
                                    my_ip.Add(ip);
                                    my_ip_prevalance.Add(1);
                                }
                            }

                            nStream.Close();

                        }
                        client.Close();
                    }
                    catch (Exception e)
                    {
                        NebliDexNetLog("Critical Node Finding IP Error: " + cn_ips[pos] + " error: " + e.ToString());
                    }
                    cn_ips.RemoveAt(pos);
                }

                //Now find most prevalent IP address, this becomes my ip
                int max_prev = 0;
                for (int i = 0; i < my_ip.Count; i++)
                {
                    if (my_ip_prevalance[i] > max_prev)
                    {
                        max_prev = my_ip_prevalance[i];
                        localip = my_ip[i];
                    }
                }
            }

            my_external_ip = localip;
            return localip;
        }

        public static void SendCNServerAction(DexConnection con, int action, string extra)
        {
            if (con.open == false) { return; } //If the connection is not open, don't send anything to it

            //Must be in try catch statement
            //Like electrum but for critical nodes
            //Make sure this is only accessed by one thread
            lock (con)
            {

                string json_encoded = "";
                if (action == 1)
                {
                    //Reflect IP of sending, for remote IP determination
                    JObject js = new JObject();
                    js["cn.method"] = "cn.reflectip";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = con.ip_address[0];
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 2)
                {
                    //This will send a list of all the connected CNs
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getlist";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    int page = Convert.ToInt32(extra); //Returns based on pages
                    JArray cndata = new JArray();
                    int pts = GetCNNodeData(ref cndata, page, false);
                    js["cn.result"] = cndata;
                    js["cn.numpts"] = pts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 3)
                {
                    //This will send the server a version of the client
                    //The connection will be terminated if it receives less than the minimum
                    con.blockdata = ""; //This will be a blocking call
                    JObject js = new JObject();
                    js["cn.method"] = "cn.myversion";
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.result"] = protocol_min_version;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 4)
                {
                    //This will send the client a response to a old version
                    //The connection will be terminated if it receives less than the minimum
                    JObject js = new JObject();
                    js["cn.method"] = "cn.myversion";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 5)
                {
                    //Pong for ping
                    //This will send the client a response to a ping
                    JObject js = new JObject();
                    js["cn.method"] = "cn.ping";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = "Pong";
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 6)
                {
                    //Request 24 hour chart data for a particular market
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.chart24h";
                    js["cn.response"] = 0;
                    js["cn.result"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 7)
                {
                    //Send the 24 hr chart data for a particular market
                    JObject js = new JObject();
                    js["cn.method"] = "cn.chart24h";
                    js["cn.response"] = 1;
                    int market = Convert.ToInt32(extra);
                    if (market < 0 || market > total_markets - 1) { return; } //Someone sent a wrong value
                    JArray chartdata = new JArray();
                    int totalpts = GetChartData(ref chartdata, market, 0); //Indicates we want this market
                    js["cn.result"] = chartdata;
                    js["cn.numpts"] = totalpts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 8)
                {
                    //Request 7 days of chart data for a particular market
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.chart7d";
                    js["cn.response"] = 0;
                    js["cn.result"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 9)
                {
                    //Send the 7 days of chart data for a particular market
                    JObject js = new JObject();
                    js["cn.method"] = "cn.chart7d";
                    js["cn.response"] = 1;
                    int market = Convert.ToInt32(extra);
                    if (market < 0 || market > total_markets - 1) { return; } //Someone sent a wrong value
                    JArray chartdata = new JArray();
                    int totalpts = GetChartData(ref chartdata, market, 1); //Indicates we want this market
                    js["cn.result"] = chartdata;
                    js["cn.numpts"] = totalpts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 10)
                {
                    //Client requesting information on open orders
                    //Because this data can be big, split into pages
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.openorders";
                    js["cn.response"] = 0;
                    string[] parts = extra.Split(':'); //First half market, second half page
                    js["cn.result"] = parts[0];
                    js["cn.page"] = parts[1];
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 11)
                {
                    //Send the open orders in cluster of 25
                    JObject js = new JObject();
                    js["cn.method"] = "cn.openorders";
                    js["cn.response"] = 1;
                    string[] parts = extra.Split(':');
                    int market = Convert.ToInt32(parts[0]);
                    int page = Convert.ToInt32(parts[1]); //Paginate results, 0 being first page
                    if (market < 0 || market > total_markets - 1) { return; } //Someone sent a wrong value
                    JArray orderdata = new JArray(); //Not a critical node
                    int totalpts = GetOrderData(ref orderdata, market, page, false); //Indicates we want this market's orders
                    js["cn.result"] = orderdata;
                    js["cn.numpts"] = totalpts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 12)
                {
                    //Request 10 recent trades for this market
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.recenttrades";
                    js["cn.response"] = 0;
                    js["cn.marketpage"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 13)
                {
                    JObject js = new JObject();
                    js["cn.method"] = "cn.recenttrades";
                    js["cn.response"] = 1;
                    string[] parts = extra.Split(':');
                    int market = Convert.ToInt32(parts[0]);
                    if (market < 0 || market > total_markets - 1) { return; } //Someone sent a wrong value
                    int page = Convert.ToInt32(parts[1]);
                    JArray recentdata = new JArray();
                    int totalpts = GetRecentTradeData(ref recentdata, market, page); //Indicates we want this market's recent trades
                    js["cn.result"] = recentdata;
                    js["cn.numpts"] = totalpts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 14)
                {
                    //Get time objects
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.syncclock";
                    js["cn.response"] = 0;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 15)
                {
                    //Return time objecct
                    JObject js = new JObject();
                    js["cn.method"] = "cn.syncclock";
                    js["cn.response"] = 1;
                    js["cn.candletime"] = next_candle_time.ToString(); //UTC Time of next candle
                    js["cn.15minterval"] = candle_15m_interval.ToString(); //Interval of 15 minutes
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 16)
                {
                    //Ping for Pong
                    //This will send the server a ping
                    JObject js = new JObject();
                    js["cn.method"] = "cn.ping";
                    js["cn.response"] = 0;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 17)
                {
                    //The client submits an order or order request
                    con.blockdata = "";
                    json_encoded = extra; //Order is already serialized
                }
                else if (action == 18)
                {
                    //The server responses to that order
                    JObject js = new JObject();
                    js["cn.method"] = "cn.sendorder";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 19)
                {
                    //The server responses to that order
                    JObject js = new JObject();
                    js["cn.method"] = "cn.sendorder";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 20)
                {
                    //The CN sends new order information to trader node
                    json_encoded = extra; //Order is already serialized
                }
                else if (action == 21)
                {
                    //The server responses to the broadcased order
                    JObject js = new JObject();
                    js["cn.method"] = "cn.neworder";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 22)
                {
                    //The client tells the server it cancelled an order, (its already canceled client side)
                    json_encoded = extra; //Order is already serialized
                }
                else if (action == 23)
                {
                    //The server sends the relay to the clients
                    json_encoded = extra;
                }
                else if (action == 24)
                {
                    //The server responses to the cancel order request
                    JObject js = new JObject();
                    js["cn.method"] = "cn.relaycancelorder";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 25)
                {
                    con.blockdata = "";
                    //The clients sends CN status to the server
                    json_encoded = extra;
                }
                else if (action == 26)
                {
                    //The server responses to the broadcased new cn request
                    JObject js = new JObject();
                    js["cn.method"] = "cn.newcn";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 27)
                {
                    //This will send a list of all the connected CNs
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getlist";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    int page = Convert.ToInt32(extra); //Returns based on pages
                    JArray cndata = new JArray();
                    int pts = GetCNNodeData(ref cndata, page, true);
                    js["cn.result"] = cndata;
                    js["cn.numpts"] = pts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 28)
                {
                    //This will request a list be sent to new CN
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getcooldownlist";
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.page"] = Convert.ToInt32(extra);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 29)
                {
                    //This will send a list of all the cool down traders
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getcooldownlist";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    int page = Convert.ToInt32(extra); //Returns based on pages
                    JArray cdata = new JArray();
                    int pts = GetCoolDownData(ref cdata, page);
                    js["cn.result"] = cdata;
                    js["cn.numpts"] = pts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 30)
                {
                    //This will request a list be sent to new CN
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getorderrequestlist";
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.page"] = Convert.ToInt32(extra);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 31)
                {
                    //This will send a list of all the order requests
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getorderrequestlist";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    int page = Convert.ToInt32(extra); //Returns based on pages
                    JArray cdata = new JArray();
                    int pts = GetOrderRequestData(ref cdata, page);
                    js["cn.result"] = cdata;
                    js["cn.numpts"] = pts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 32)
                {
                    //This will request a CN list be sent to new CN
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getlist";
                    js["cn.authlevel"] = 1;
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.page"] = Convert.ToInt32(extra);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 33)
                {
                    //This will request a list of the last chart prices
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getchartprices";
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.timepage"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 34)
                {
                    //Client requesting information on open orders
                    //Because this data can be big, split into pages
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getchartprices";
                    js["cn.response"] = 1;
                    string[] parts = extra.Split(':'); //First half market, second half page
                    int time = Convert.ToInt32(parts[0]);
                    int page = Convert.ToInt32(parts[1]);

                    JArray cdata = new JArray();
                    int pts = GetChartPricesData(ref cdata, time, page);
                    js["cn.result"] = cdata;
                    js["cn.numpts"] = pts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 35)
                {
                    //This will request the volume for a market
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getvolume";
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.market"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 36)
                {
                    //This will return the volume
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getvolume";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    int market = Convert.ToInt32(extra);
                    decimal[] volume = new decimal[2];
                    volume = GetMarketVolume(market);
                    js["cn.trade_volume"] = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", volume[0]);
                    js["cn.base_volume"] = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", volume[1]);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 37)
                {
                    //The server responses to that order request
                    JObject js = new JObject();
                    js["cn.method"] = "cn.sendorderrequest";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 38)
                {
                    //The server responses to the broadcased new order request
                    JObject js = new JObject();
                    js["cn.method"] = "cn.relayorderrequest";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 39)
                {
                    //The server is telling its connected TNs that an order is now pending
                    JObject js = new JObject();
                    js["cn.method"] = "cn.pendorder";
                    js["cn.response"] = 0; //This is telling the CN that this is not a response
                    js["cn.result"] = extra; //This is the order nonce
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 40)
                {
                    //The CN send this message to the selected TN involved in the trade
                    json_encoded = extra;
                }
                else if (action == 41)
                {
                    //The server responses to the broadcased trade relay
                    JObject js = new JObject();
                    js["cn.method"] = "cn.relaytradeavail";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 42)
                {
                    //The client is requesting a validation node
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getvalidator";
                    js["cn.response"] = 0;
                    js["cn.order_nonce"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 43)
                {
                    //The server responses to the order request availability
                    JObject js = new JObject();
                    js["cn.method"] = "cn.orderrequestexist";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 44)
                {
                    //The server responses to the broadcased validator relay
                    JObject js = new JObject();
                    js["cn.method"] = "cn.relayvalidator";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 45)
                {
                    //The server responses that info was not found
                    JObject js = new JObject();
                    js["cn.method"] = "cn.relayvalidator";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 46)
                {
                    //Same as 45 but found info and used for prefilled validator info
                    json_encoded = extra;
                }
                else if (action == 47)
                {
                    //Same as 46 but for retrieving block data
                    con.blockdata = "";
                    json_encoded = extra;
                }
                else if (action == 48)
                {
                    //Request the last price
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getlastprice";
                    js["cn.response"] = 0;
                    js["cn.market"] = extra;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 49)
                {
                    //The server responses with the last price information
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getlastprice";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    int market = Convert.ToInt32(extra);
                    js["cn.price"] = String.Format(CultureInfo.InvariantCulture, "{0:0.########}", GetMarketLastPrice(market)); //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 50)
                {
                    //CN requesting information on open orders
                    //Because this data can be big, split into pages
                    con.blockdata = "";//This will be a blocking call more than likely
                    JObject js = new JObject();
                    js["cn.method"] = "cn.openorders";
                    js["cn.response"] = 0;
                    string[] parts = extra.Split(':'); //First half market, second half page
                    js["cn.result"] = parts[0];
                    js["cn.page"] = parts[1];
                    js["cn.authlevel"] = 1;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 51)
                {
                    //Send the open orders in cluster of 25
                    JObject js = new JObject();
                    js["cn.method"] = "cn.openorders";
                    js["cn.response"] = 1;
                    string[] parts = extra.Split(':');
                    int market = Convert.ToInt32(parts[0]);
                    int page = Convert.ToInt32(parts[1]); //Paginate results, 0 being first page
                    if (market < 0 || market > total_markets - 1) { return; } //Someone sent a wrong value
                    JArray orderdata = new JArray();
                    int totalpts = GetOrderData(ref orderdata, market, page, true); //Indicates we want this market's orders
                    js["cn.result"] = orderdata;
                    js["cn.numpts"] = totalpts;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 52)
                {
                    //Request NDEX fee
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getndexfee";
                    js["cn.response"] = 0;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 53)
                {
                    //Send the NDEX fee
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getndexfee";
                    js["cn.response"] = 1;
                    js["cn.ndexfee"] = ndex_fee.ToString(CultureInfo.InvariantCulture);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 54)
                {
                    //The server responses with its status
                    JObject js = new JObject();
                    js["cn.method"] = "cn.status";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = extra; //The order is in this string
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 55)
                {
                    //Return my version of NebliDex
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getversion";
                    js["cn.response"] = 1; //This is telling the CN that this is a response
                    js["cn.result"] = protocol_version;
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 56)
                {
                    //This will request a CN list be sent to newly connected TN
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getlist";
                    js["cn.response"] = 0; //This is telling the CN that this is a response
                    js["cn.page"] = Convert.ToInt32(extra);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 57)
                {
                    //RPC request server actions
                    //Let the client know if our local blockhelper is active
                    JObject js = new JObject();
                    js["cn.method"] = "cn.getblockhelper_status";
                    js["cn.response"] = 1; //This is telling the client that this is a response
                    if (using_blockhelper == true)
                    {
                        js["cn.result"] = "Active";
                    }
                    else
                    {
                        js["cn.result"] = "Not Active";
                    }
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 58)
                {
                    //Same as 46 but remove requirement to write log
                    json_encoded = extra;
                }

                if (json_encoded.Length > 0)
                {
                    //Now add data length and send data
                    Byte[] data = System.Text.Encoding.ASCII.GetBytes(json_encoded);
                    uint data_length = (uint)json_encoded.Length;
                    try
                    {
                        con.stream.Write(Uint2Bytes(data_length), 0, 4); //Write the length of the bytes to be received
                        con.stream.Write(data, 0, data.Length);
                        if (action != 58)
                        {
                            //BlockHelper uses a lot of messages so don't write to log
                            NebliDexNetLog("Sent msg: " + json_encoded);
                        }
                    }
                    catch (Exception e)
                    {
                        NebliDexNetLog("Connection Disconnected: " + con.ip_address[0] + ", error: " + e.ToString());
                        con.open = false;
                    }
                }

                con.lasttouchtime = UTCTime(); //Update the lasttouchtime
            }
        }

		public static void ToggleCNStatus(IntroWindow win)
        {
            //Modify the status of the Window when needed
            //This function will toggle CN status and show information on charts
            //Steps of method
            //Broadcast new CN status to connected CN
            //CNs validate this status as message relays (by checking blockchain) across network
            //After confirmed validation, new CN will clear chart data
            //Then get chart data for all markets, open orders for all markets
            //All recent trades for all markets, cooldown traders, order requests, CN list (should have new CN on it)
            //The new CN will not verify each CN balance at once but check random CNs balance occasionally
            //At most 15 minute intervals, also check if CN is online (if not online, will remove from list)

            if (critical_node == false)
            {
                UpdateCNStatusWindow(win, "Requesting Critical Node Status");
                if (GetWalletAmount(3) < cn_ndex_minimum)
                {
					if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
                            MessageBox(win, "Notice", "You do not have enough NDEX to become a Critical Node (" + App.cn_ndex_minimum + ")", "OK");
                            CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
                        Console.WriteLine("You do not have enough NDEX to become a Critical Node (" + cn_ndex_minimum + ")");
                    }
                    return;
                }

                bool firstnode = false;
                if (CN_Nodes_By_IP.Count == 0)
                {
                    firstnode = true;
                    //There are no critical nodes available. Become the first
                }

                DexConnection dex = null;
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
                if (dex == null && firstnode == false)
                {
                    CloseCNStatusWindow(win);
                    return;
                }

                //Get information needed to be identified as CN
                string my_ip = getPublicFacingIP(); //Get my IP address
                if (my_ip == "")
                {
					if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
                            MessageBox(win, "Notice", "Could not resolve your Public IP address", "OK");
                            CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
                        Console.WriteLine("Could not resolve your Public IP address");
                    }
                    return;
                }
                string mypubkey = ""; //The public key for the NEBL wallet
                string sig = "";
                Wallet neblwall = null;

                for (int i = 0; i < WalletList.Count; i++)
                {
                    if (WalletList[i].type == 0)
                    {
                        //Neblio wallet
                        neblwall = WalletList[i]; break;
                    }
                }

                lock (transactionLock)
                {
                    //Prevent more than one thread from using the network
                    Network my_net;
                    if (testnet_mode == false)
                    {
                        my_net = Network.Main;
                    }
                    else
                    {
                        my_net = Network.TestNet;
                    }

                    //Change the network
                    ChangeVersionByte(3, ref my_net); //NEBL network
                    ExtKey priv_key = ExtKey.Parse(neblwall.private_key, my_net);
                    mypubkey = priv_key.PrivateKey.PubKey.ToString(); //Will transmit the pubkey
                    sig = priv_key.PrivateKey.SignMessage(my_ip); //Sign the IP address, so no one can change it
                }

                lock (CN_Nodes_By_IP)
                {
                    foreach (CriticalNode node in CN_Nodes_By_IP.Values)
                    {
                        node.lastchecked = UTCTime(); //Try to prevent the system from checking these nodes before they have a signature
                    }
                }

                //Turn our server on to receive connections
                ToggleCriticalNodeServer(true);
                lastvalidate_time = UTCTime();
                LastNetworkQueryTime = -1; //Reset the lastnetworkquerytime

                if (critical_node == false)
                {
					//Failed to start critical node
                    if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
                            MessageBox(win, "Notice", "The Critical Node port is already occupied. Port: " + critical_node_port, "OK");
                            CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
                        Console.WriteLine("The Critical Node port is already occupied. Port: " + critical_node_port);
                    }
                    return;
                }

                if (firstnode == true)
                {
                    CriticalNode mynode = new CriticalNode();
                    mynode.ip_add = my_ip;
                    mynode.lastchecked = UTCTime();
                    mynode.ndex = GetWalletAmount(3);
                    mynode.pubkey = mypubkey;
                    mynode.signature_ip = sig;
                    CN_Nodes_By_IP.Add(my_ip, mynode); //This is the first node

                    //Try to connect the BlockHelper RPC
                    ConnectBlockHelperNode();
                    if (run_headless == true && using_blockhelper == true)
                    {
                        Console.WriteLine("Connected to NebliDex BlockHelper");
                    }

					if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
                            MessageBox(win, "Notice", "You are the first Critical Node on the network.\nPlease do not close the program when validating transactions.", "OK");
                            CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
                        Console.WriteLine("You are the first Critical Node on the network.\nPlease do not close the program when validating transactions.");
                    }
                    RecalculateCNWeight();
                    return;
                }

                //Try to connect the BlockHelper RPC
                ConnectBlockHelperNode();
                if (run_headless == true && using_blockhelper == true)
                {
                    Console.WriteLine("Connected to NebliDex BlockHelper");
                }

                //Now submit this request to the server
                JObject js = new JObject();
                js["cn.method"] = "cn.newcn";
                js["cn.response"] = 0;
                js["cn.authlevel"] = 2;
                js["cn.ip"] = my_ip;
                js["cn.pubkey"] = mypubkey; //Pubkey can be used to deduce a NEBL address
                js["cn.sig"] = sig;
                js["cn.version"] = protocol_min_version; //CN will be rejected across network if its min_version lower than CN min_version
                js["cn.time"] = UTCTime(); //This should be within 15 seconds of the receiving CN

                //This method will be rebroadcasted across the network to all CNs but first to the connected CN
                string blockdata = "";
                lock (dex.blockhandle)
                {
                    try
                    {
                        dex.blockhandle.Reset();
                        SendCNServerAction(dex, 25, JsonConvert.SerializeObject(js));
                        //Wait for a response, if accepted or not
                        dex.blockhandle.WaitOne(10000); //Wait 10 seconds           
                    }
                    catch (Exception)
                    {
                        dex.blockdata = "";
                    }
                    blockdata = dex.blockdata;
                }

                if (blockdata == "")
                {
                    ToggleCriticalNodeServer(false); //Turn server back off
					if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
                            MessageBox(win, "Error!", "Could not connect to CN", "OK");
                            CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
                        Console.WriteLine("Could not connect to CN");
                    }
                    return;
                }

                //You will be rejected if the CN you are connected to cannot connect to your server

                if (blockdata != "CN Accepted")
                {
                    ToggleCriticalNodeServer(false); //Turn server back off
                    bool error_ok = CheckErrorMessage(blockdata); //Check error message
					if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
							if (error_ok == true)
							{
								MessageBox(win, "Notice", "Your Critical Node status request has been rejected by the Critical Node\n"+blockdata, "OK");
							}
							CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
						if (error_ok == true)
						{
							Console.WriteLine("Your Critical Node status request has been rejected by the Critical Node");
							Console.WriteLine(blockdata);
						}
					}
                    return;
                }

                //You have been added as a critical node (congrats)
                critical_node_pending = true; //Prevents this node from relaying/receiving orders
                                              //You may start to receive messages before 
                UpdateCNStatusWindow(win, "Critical Node Status Accepted: Retrieving Data");

                ExchangeWindow.ClearMarketData(exchange_market); //Remove the current chart data
                GetCNAllMarketData(dex); //Will retrieve information from all markets including own critical node status
                if (critical_node_pending == true)
                {
                    //Unable to get all the market data, stop the connection
                    ToggleCriticalNodeServer(false); //Turn server back off
                    critical_node_pending = false;
					if (run_headless == false)
                    {
                        Application.Invoke(delegate
                        {
                            MessageBox(win, "Notice", "Your Critical Node status request was rejected because the connection was interrupted. Please try again.", "OK");
                            CloseCNStatusWindow(win);
                        });
                    }
                    else
                    {
                        Console.WriteLine("Your Critical Node status request was rejected because the connection was interrupted. Please try again.");
                    }
                    return;
                }

				if (run_headless == false)
                {
                    Application.Invoke(delegate
                    {
                        MessageBox(win, "Notice", "You are now a Critical Node.\nPlease do not close the program when validating transactions.", "OK");
                        CloseCNStatusWindow(win);
                    });
                }
                else
                {
                    Console.WriteLine("You are now a Critical Node.\nPlease do not close the program when validating transactions.");
                }

                return;
            }
            else
            {
                if (cn_num_validating_tx > 0)
                {
					//You are still currently validating transactions for can't close mode
                    Application.Invoke(delegate
                    {
                        //Must be on UI thread
                        MessageBox(win, "Notice", "You are still validating transactions. Please wait before closing Critical Node.", "OK");
                        CloseCNStatusWindow(win);
                    });
                    return;
                }

                UpdateCNStatusWindow(win, "Disabling Critical Node Status");
                ToggleCriticalNodeServer(false); //Deactivate the critical node

                //Request the data from 
                ExchangeWindow.ClearMarketData(-1);
                GetCNMarketData(exchange_market);

                CloseCNStatusWindow(win);
                return;
            }
        }

        public static void RebroadcastCNStatus()
        {
            NebliDexNetLog("Attempting to re-establish CN status");

            //First find a connection to a CN
            DexConnection dex = null;
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

            if (dex == null) { return; } //Couldn't find an open connection

            string my_ip = getPublicFacingIP();
            if (CN_Nodes_By_IP.ContainsKey(my_ip) == false)
            {
                //Not a CN anyway
                return;
            }

            if (CN_Nodes_By_IP[my_ip].signature_ip == null) { return; }
            if (CN_Nodes_By_IP[my_ip].signature_ip.Length == 0) { return; }
            for (int i = 0; i < WalletList.Count; i++)
            {
                if (WalletList[i].type == 3)
                {
                    if (WalletList[i].status == 2) { return; } //We can't broadcast with this wallet while we are waiting
                    break;
                }
            }

            if (ntp1downcounter > 1) { return; } //Do not try to rebroadcast connection while the API server is down

            CriticalNode my_node = CN_Nodes_By_IP[my_ip];

            string my_sig = CN_Nodes_By_IP[my_ip].signature_ip;
            string my_pubkey = CN_Nodes_By_IP[my_ip].pubkey;

            //In case we are removed from the network for who knows why from some CNs
            JObject js = new JObject();
            js["cn.method"] = "cn.newcn";
            js["cn.response"] = 0;
            js["cn.authlevel"] = 2;
            js["cn.ip"] = my_ip;
            js["cn.pubkey"] = my_pubkey; //Pubkey can be used to deduce a NEBL address
            js["cn.sig"] = my_sig;
            js["cn.version"] = protocol_min_version; //CN will be rejected across network if lower than min_version
            js["cn.time"] = UTCTime();

            //This method will be rebroadcasted across the network to all CNs but first to the connected CN
            string blockdata = "";

            //Tell us that we are rebroadcasting the node
            CN_Nodes_By_IP[my_ip].rebroadcast = true;

            //We will receive a new CN method that we will reject because its the same IP as me
            lock (dex.blockhandle)
            {
                try
                {
                    dex.blockhandle.Reset();
                    SendCNServerAction(dex, 25, JsonConvert.SerializeObject(js));
                    //Wait for a response, if accepted or not
                    dex.blockhandle.WaitOne(10000); //Wait 10 seconds           
                }
                catch (Exception)
                {
                    dex.blockdata = "";
                }
                blockdata = dex.blockdata;
            }

            if (blockdata == "") { return; } //Lost connection to the network
            reconnect_cn = false;

            CN_Nodes_By_IP[my_ip].rebroadcast = false;

            //You will be rejected if the CN you are connected to cannot connect to your server
            if (blockdata != "CN Accepted")
            {
                ToggleCriticalNodeServer(false); //Turn server back off
				NebliDexNetLog(blockdata);
                if (run_headless == false)
                {
                    Application.Invoke(delegate
                    {
                        if (main_window_loaded == true)
                        {
                            MessageBox(main_window, "Notice", "Could not re-establish your critical node status.", "OK");
                            main_window.ToggleCNInfo(false);
                        }
                    });
                }
                else
                {
                    Console.WriteLine("Could not re-establish your critical node status. Closing Program.");
                    Headless_Application_Close();
                }
                return;
            }

            //Now grab the list of all the CNs and add ones that we do not already have
            if (cn_network_down == false)
            {
                GetRemoteCNList(dex);
            }
            else
            {
                NebliDexNetLog("Internet was down or chart out of sync so must grab all market data");
                critical_node_pending = true; //Do not receive any data
                                              //The internet was down, meaning the chart/order data may be out of sync, resync the data
                ExchangeWindow.ClearMarketData(-1); //Remove all market data
                GetCNAllMarketData(dex);
                if (critical_node_pending == false)
                {
                    cn_network_down = false; //Everything is good now
					Application.Invoke(delegate
                    {
                        if (main_window_loaded == true)
                        {
                            main_window.RefreshUI(); //Reload the data
                        }
                    });
                }
                else
                {
                    //Failed to get all the data from the connected CN
                    reconnect_cn = true; //Try to connect again
                }
            }

        }

        public static string EvaluateCNRequest(JObject js, bool cnmode)
        {
            //First check to see if we already received this message            
            if (js["relay.nonce"] == null)
            {
                js["relay.nonce"] = GenerateHexNonce(24);
                js["relay.timestamp"] = UTCTime();
            }
            else
            {
                bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                if (received == true) { return "CN Rejected Or Already Accepted"; } //Already received this message
            }

            string ip = js["cn.ip"].ToString();
            string pubkey = js["cn.pubkey"].ToString();
            string sig = js["cn.sig"].ToString();

            int cn_version = Convert.ToInt32(js["cn.version"].ToString());
            if (cn_version < protocol_min_version)
            {
                return "CN Rejected: Client Version Too Old"; //Do not add a CN that has min_version less than the minimum version supported
            }

            //First check to see if ip is on the blacklist
            bool blacklisted = CheckCNBlacklist(ip);
            if (blacklisted == true)
            {
                return "CN Rejected: Client Found on CN Blacklist";
            }

            string my_ip = getPublicFacingIP();
            if (ip.Equals(my_ip) == true)
            {
                //This CN request is on the same network as my IP address, not allowed
                return "CN Rejected: Client has same IP as active CN";
            }

            //Now verify if the message matches the sig using the pubkey
            PubKey pub = new PubKey(pubkey);
            bool check = pub.VerifyMessage(ip, sig);
            if (check == false)
            {
                //Message was altered
                return "CN Rejected: Signature Verification Failed";
            }

            //Generate a NEBL address from the public key
            string ndex_add;
            lock (transactionLock)
            {
                Network my_net;
                if (testnet_mode == false)
                {
                    my_net = Network.Main;
                }
                else
                {
                    my_net = Network.TestNet;
                }
                ChangeVersionByte(3, ref my_net);
                ndex_add = pub.GetAddress(my_net).ToString();
            }

            //Now verify the ndex balance using ntp1node
            decimal balance = GetBlockchainAddressBalance(3, ndex_add, false);
            if (balance < cn_ndex_minimum)
            {
                //Not enough balance to be a CN
                return "CN Rejected: NDEX balance below CN minimum";
            }

            //Now check to see if server is available at address
            if (cnmode == false)
            {

                //First check if the timestamp is close to mine (should be)
                int ctime = UTCTime();
                int newcn_time = 0;
                newcn_time = Convert.ToInt32(js["cn.time"].ToString());
                if (Math.Abs(newcn_time - ctime) > 30)
                {
                    NebliDexNetLog("New CN Server time is not accurate");
                    return "CN Rejected: Client time is not synced with active CN time";
                }

                bool connected = false;
                //Only check if client server is online from the CN who first received the request
                try
                {
                    IPAddress address = IPAddress.Parse(ip);
                    int port = critical_node_port;
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, port, 5))
                    {
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        JObject vob = new JObject();
                        vob["cn.method"] = "cn.newcnstatus";
                        vob["cn.response"] = 0;

                        string json_encoded = JsonConvert.SerializeObject(vob);
                        string response = CNWaitResponse(nStream, client, json_encoded);

                        NebliDexNetLog("New CN Response: " + response);

                        JObject result = JObject.Parse(response);
                        if (result["cn.result"].ToString() == "New CN Pending")
                        {
                            //Connection was successful
                            connected = true;
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
                    NebliDexNetLog("Unable to connect to the client potential CN: " + ip + ", error: " + e.ToString());
                }

                if (connected == false)
                {
                    return "CN Rejected: Unable to connect to your Client. Check your firewall.";
                }
            }

            uint prev_strike = 0;
            if (CN_Nodes_By_IP.ContainsKey(ip) == true)
            {
                prev_strike = CN_Nodes_By_IP[ip].strikes; //Prevent the CN from gaming the strikes
            }

            //Everything is good, add the CN to our nodes
            CriticalNode node = new CriticalNode();
            node.ip_add = ip;
            node.ndex = balance; //The amount of ndex
            node.pubkey = pubkey;
            node.signature_ip = sig;
            node.strikes = prev_strike;
            node.lastchecked = UTCTime();

            lock (CN_Nodes_By_IP)
            {
                CN_Nodes_By_IP[ip] = node; //This will add or replace the node in the dictionary

                //Remove other CNs that match the pubkey but not the IP to prevent CN duplication
                Dictionary<string, CriticalNode> Local_CN_Nodes_By_IP = new Dictionary<string, CriticalNode>(CN_Nodes_By_IP);
                foreach (CriticalNode cn in Local_CN_Nodes_By_IP.Values)
                {
                    if (cn.ip_add.Equals(ip) == false && cn.pubkey.Equals(pubkey) == true)
                    {
                        //Same pubkey but different IP address, remove the duplicates
                        NebliDexNetLog("Removing duplicate CN: " + cn.ip_add);
                        CN_Nodes_By_IP.Remove(cn.ip_add); //Take it out
                    }
                }
            }

            RecalculateCNWeight();
            RecreateCNList();
            return ""; //Blank means good
        }

        public static void CalculateCNFee()
        {
            //The fee is calculated based on last two 24 hr candles in the NDEX/NEBL market
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Search the database for data points that fit this specification
            int backtime = App.UTCTime() - 60 * 270; //Go back at most 270 minutes
            string myquery = "Select close From CANDLESTICKS7D Where market = 2 And utctime > @time Order By utctime DESC Limit 2";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            int count = 0;
            double average = 0;
            while (statement_reader.Read())
            {
                count++;
                average = average + Convert.ToDouble(statement_reader["close"].ToString(), CultureInfo.InvariantCulture);
            }
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();

            //Now calculate the fee
            double price = 0.005; //This is the default ndex price based on ICO price
            if (count == 2)
            {
                //We need 2 candles to calculate the fee
                price = average / Convert.ToDouble(count);
                if (price == 0) { price = 0.005; }
            }
            double fee = 0.005 / price * 10;
            //Round fee to nearest even whole number
            fee = Math.Round(fee / 2.0) * 2;
            if (fee > 100) { fee = 100; }
            if (fee < 2) { fee = 2; }
            ndex_fee = Convert.ToDecimal(fee); //Update the NDEX fee
        }

        //Chart, Order & Axillary Data to transmit      
        public static decimal[] GetMarketVolume(int market)
        {
            //This will use the recent trade data to get rolling data of market volume
            //Will return trade amount and base amount
            decimal[] vol = new decimal[2];
            vol[0] = 0; //Trade amount
            vol[1] = 0; //Base amount
            if (market < 0 || market >= total_markets) { return vol; } //Not valid market
            lock (RecentTradeList[market])
            {
                int time = UTCTime() - 60 * 60 * 24; //Go back a day
                for (int i = 0; i < RecentTradeList[market].Count; i++)
                {
                    if (RecentTradeList[market][i].utctime > time)
                    {
                        //Within last 24 hrs
                        vol[0] += RecentTradeList[market][i].amount; //Trade volume
                        vol[1] += Math.Round(RecentTradeList[market][i].amount * RecentTradeList[market][i].price, 8); //Base volume
                    }
                }
            }

            return vol;
        }

        public static decimal GetMarketLastPrice(int market)
        {
            //The price within the last 24 hours or 0 if there is no activity
            decimal price = 0;
            if (market < 0 || market >= total_markets) { return 0; } //Not valid market

            //Do not use recenttradelist as that erases every 24 hours, use chartlastprice, more permanent
            lock (ChartLastPrice)
            {
                if (ChartLastPrice[0].Count == 0) { return 0; } //There are no data points
                                                                //Most recent price is last
                for (int i = ChartLastPrice[0].Count - 1; i >= 0; i--)
                {
                    if (ChartLastPrice[0][i].market == market)
                    {
                        //Same market, use this price
                        price = ChartLastPrice[0][i].price;
                        break;
                    }
                }
            }
            return price;
        }

        public static int GetCNNodeData(ref JArray cndata, int page, bool cnmode)
        {

            int offset = page * 25; //We will paginate the data
            if (offset < 0 || offset > CN_Nodes_By_IP.Count) { return 0; }

            int ccount = 0;
            int index = 0;
            lock (CN_Nodes_By_IP)
            {
                foreach (CriticalNode node in CN_Nodes_By_IP.Values)
                {
                    if (index >= offset)
                    {
                        if (ccount >= 25) { break; }
                        JObject ob = new JObject();
                        ob["cn.ip"] = node.ip_add;
                        if (cnmode == true)
                        {
                            if (node.signature_ip != null)
                            { //Do not send null signature nodes
                                ob["cn.sig"] = node.signature_ip;
                                ob["cn.ndex"] = node.ndex.ToString(CultureInfo.InvariantCulture);
                                ob["cn.pubkey"] = node.pubkey;
                                ob["cn.strikes"] = node.strikes;
                                cndata.Add(ob);
                                ccount++;
                            }
                        }
                        else
                        {
                            cndata.Add(ob);
                            ccount++;
                        }
                    }
                    index++;
                }
            }

            return ccount;
        }

        public static int GetCoolDownData(ref JArray cdata, int page)
        {

            int offset = page * 25; //We will paginate the data
            if (offset < 0 || offset > CoolDownList.Count) { return 0; }

            int ccount = 0;
            lock (CoolDownList)
            {
                for (int i = offset; i < CoolDownList.Count; i++)
                {
                    if (ccount >= 25) { break; }
                    JObject ob = new JObject();
                    ob["address"] = CoolDownList[i].address;
                    ob["cointype"] = CoolDownList[i].cointype;
                    ob["time"] = CoolDownList[i].utctime;
                    cdata.Add(ob);
                    ccount++;
                }
            }

            return ccount;
        }

        public static int GetChartPricesData(ref JArray cdata, int time, int page)
        {

            int offset = page * 25; //We will paginate the data
            if (offset < 0 || offset > ChartLastPrice[time].Count) { return 0; }

            int ccount = 0;
            lock (ChartLastPrice)
            {
                //Oldest price to most recent
                for (int i = offset; i < ChartLastPrice[time].Count; i++)
                {
                    if (ccount >= 25) { break; }
                    JObject ob = new JObject();
                    ob["market"] = ChartLastPrice[time][i].market;
                    ob["price"] = ChartLastPrice[time][i].price.ToString(CultureInfo.InvariantCulture);
                    ob["atime"] = ChartLastPrice[time][i].atime;
                    cdata.Add(ob);
                    ccount++;
                }
            }

            return ccount;
        }

        public static int GetOrderRequestData(ref JArray cdata, int page)
        {

            int offset = page * 10; //We will paginate the data
            if (offset < 0 || offset > OrderRequestList.Count) { return 0; }

            int ccount = 0;
            lock (OrderRequestList)
            {
                for (int i = offset; i < OrderRequestList.Count; i++)
                {
                    if (ccount >= 10) { break; } //Only 10 at a time
                    JObject ob = new JObject();
                    ob["request.order_nonce"] = OrderRequestList[i].order_nonce_ref;
                    ob["request.request_id"] = OrderRequestList[i].request_id;
                    ob["request.market"] = OrderRequestList[i].market;
                    ob["request.time"] = OrderRequestList[i].utctime;
                    ob["request.type"] = OrderRequestList[i].type; //Opposite of the Order type
                    ob["request.stage"] = OrderRequestList[i].order_stage;
                    ob["request.validator_pubkey"] = OrderRequestList[i].validator_pubkey;
                    ob["request.ndex_fee"] = OrderRequestList[i].ndex_fee.ToString(CultureInfo.InvariantCulture);
                    ob["request.who_validate"] = OrderRequestList[i].who_validate;

                    ob["request.from_add_1"] = OrderRequestList[i].from_add_1;
                    ob["request.to_add_1"] = OrderRequestList[i].to_add_1;
                    ob["request.amount_1"] = OrderRequestList[i].amount_1.ToString(CultureInfo.InvariantCulture);
                    ob["request.from_add_2"] = OrderRequestList[i].from_add_2;
                    ob["request.to_add_2"] = OrderRequestList[i].to_add_2;
                    ob["request.amount_2"] = OrderRequestList[i].amount_2.ToString(CultureInfo.InvariantCulture);

                    ob["request.custodial_add"] = OrderRequestList[i].custodial_add;
                    ob["request.taker_ip"] = OrderRequestList[i].ip_address_taker[0];
                    ob["request.taker_port"] = OrderRequestList[i].ip_address_taker[1];
                    ob["request.maker_ip"] = OrderRequestList[i].ip_address_maker[0];
                    ob["request.maker_port"] = OrderRequestList[i].ip_address_maker[1];

                    //Cn informatoin
                    ob["request.maker_cn_ip"] = OrderRequestList[i].maker_cn_ip;
                    ob["request.taker_cn_ip"] = OrderRequestList[i].taker_cn_ip;

                    cdata.Add(ob);
                    ccount++;
                }
            }

            return ccount;
        }

        public static int GetChartData(ref JArray mydata, int market, int chart)
        {
            //This function is for a critical node
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Search the database for data points that fit this specification
            int backtime = 0;
            string myquery = "";

            //Regular nodes clear their chart data/open orders with every market switch

            if (chart == 0)
            {
                //24 h chart
                backtime = App.UTCTime() - 60 * 60 * 25; //Get 1500 minutes worth of data, slightly more than a day
                myquery = "Select highprice, lowprice, open, close, utctime From CANDLESTICKS24H Where market = @mymarket And utctime > @time Order By utctime ASC Limit 100"; //Show results from oldest to most recent
            }
            else if (chart == 1)
            {
                //7 day chart
                backtime = App.UTCTime() - (int)Math.Round(60.0 * 60.0 * 24.0 * 6.25);
                myquery = "Select highprice, lowprice, open, close, utctime From CANDLESTICKS7D Where market = @mymarket And utctime > @time Order By utctime ASC Limit 100"; //Show results from oldest to most recent
            }

            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", backtime);
            statement.Parameters.AddWithValue("@mymarket", market);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            int count = 0;
            while (statement_reader.Read())
            {
                count++;
                JObject ob = new JObject();
                //Read the data
                ob["hi"] = statement_reader["highprice"].ToString();
                ob["lo"] = statement_reader["lowprice"].ToString();
                ob["cl"] = statement_reader["close"].ToString();
                ob["op"] = statement_reader["open"].ToString();
                ob["ut"] = statement_reader["utctime"].ToString();
                mydata.Add(ob); //Add a row
            }

            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
            return count;
        }

        public static int GetRecentTradeData(ref JArray mydata, int market, int page)
        {

            int offset = page * 10; //We will paginate the data
            if (offset < 0 || offset >= RecentTradeList[market].Count) { return 0; }

            int rcount = 0;
            lock (RecentTradeList[market])
            {
                //Send only 10 recent trades
                //Most recent trade list is arranged newest to oldest
                for (int i = offset; i < RecentTradeList[market].Count; i++)
                {
                    if (rcount > 10) { break; }
                    JObject ob = new JObject();
                    ob["time"] = RecentTradeList[market][i].utctime.ToString();
                    ob["mark"] = RecentTradeList[market][i].market.ToString();
                    ob["type"] = RecentTradeList[market][i].type.ToString();
                    ob["price"] = RecentTradeList[market][i].price.ToString(CultureInfo.InvariantCulture);
                    ob["amount"] = RecentTradeList[market][i].amount.ToString(CultureInfo.InvariantCulture);
                    rcount++;
                    mydata.Add(ob);
                }
            }

            return rcount;
        }

        public static int GetOrderData(ref JArray mydata, int market, int page, bool cn_mode)
        {
            //Fairly basic function that converts list of open orders to json
            //Because this data will be accessed async, must lock list

            int offset = page * 25;

            int ocount = 0;
            lock (OpenOrderList[market])
            {
                //Make sure not to send all open orders at once
                if (offset < 0 || offset >= OpenOrderList[market].Count) { return 0; } //No more results
                                                                                       //Send in groups of 25 orders
                for (int i = offset; i < OpenOrderList[market].Count; i++)
                {
                    if (i >= offset + 25) { break; } //Break every 25 orders
                    JObject ob = new JObject();
                    ob["nonce"] = OpenOrderList[market][i].order_nonce;
                    ob["mark"] = OpenOrderList[market][i].market.ToString();
                    ob["type"] = OpenOrderList[market][i].type.ToString();
                    ob["price"] = OpenOrderList[market][i].price.ToString(CultureInfo.InvariantCulture);
                    ob["amount"] = OpenOrderList[market][i].amount.ToString(CultureInfo.InvariantCulture);
                    ob["min_amount"] = OpenOrderList[market][i].minimum_amount.ToString(CultureInfo.InvariantCulture);
                    ob["original"] = OpenOrderList[market][i].original_amount.ToString(CultureInfo.InvariantCulture);
                    ob["stage"] = OpenOrderList[market][i].order_stage.ToString();
                    ob["cool"] = OpenOrderList[market][i].cooldownend.ToString();
                    if (cn_mode == true)
                    {
                        //Include IPs and Ports
                        ob["ip_add"] = OpenOrderList[market][i].ip_address_port[0];
                        ob["port"] = OpenOrderList[market][i].ip_address_port[1];
                        ob["cn_ip"] = OpenOrderList[market][i].cn_relayer_ip;
                    }
                    ocount++;
                    mydata.Add(ob);
                }
            }

            return ocount;
        }

        public static void GetCNAllMarketData(DexConnection dex)
        {
            //We need to block CN data from coming to server until we are fully connected. Charts loaded and all.

            int scan_markets = total_scan_markets; //This is usually the same as total markets but when we are updating it can be different

            //Chart Data for all Markets
            for (int market = 0; market < scan_markets; market++)
            {
                for (int i = 0; i < 2; i++)
                {
                    //Do this for the 24h and 7D
                    try
                    {
                        string blockdata = "";
                        lock (dex.blockhandle)
                        {
                            dex.blockhandle.Reset();
                            if (i == 0)
                            {
                                SendCNServerAction(dex, 6, "" + market); //Get the 24 hour chart first
                            }
                            else if (i == 1)
                            {
                                SendCNServerAction(dex, 8, "" + market); //Get the 7 day chart
                            }
                            dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                            if (dex.blockdata == "") { return; }
                            blockdata = dex.blockdata;
                        }

                        JObject js = JObject.Parse(blockdata);
                        int totalpts = 0;
                        if (js["cn.numpts"] != null)
                        {
                            totalpts = Convert.ToInt32(js["cn.numpts"].ToString());
                        }
                        else
                        {
                            return; //Someone went wrong with lookup
                        }
                        if (totalpts > 0)
                        {
                            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
                            mycon.Open();

                            //Set our busy timeout, so we wait if there are locks present
                            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
                            statement.ExecuteNonQuery();
                            statement.Dispose();

                            statement = new SqliteCommand("BEGIN TRANSACTION", mycon); //Create a transaction to make inserts faster
                            statement.ExecuteNonQuery();
                            statement.Dispose();
                            string myquery = "";
                            foreach (JToken row in js["cn.result"])
                            {
                                //Add each row to our table
                                if (i == 0)
                                {
                                    myquery = "Insert Into CANDLESTICKS24H (utctime, market, highprice, lowprice, open, close)";
                                }
                                else if (i == 1)
                                {
                                    myquery = "Insert Into CANDLESTICKS7D (utctime, market, highprice, lowprice, open, close)";
                                }
                                myquery += " Values (@time, @market, @high, @low, @op, @clo);";
                                statement = new SqliteCommand(myquery, mycon);
                                statement.Parameters.AddWithValue("@time", row["ut"].ToString());
                                statement.Parameters.AddWithValue("@market", market);
                                statement.Parameters.AddWithValue("@high", row["hi"].ToString());
                                statement.Parameters.AddWithValue("@low", row["lo"].ToString());
                                statement.Parameters.AddWithValue("@op", row["op"].ToString());
                                statement.Parameters.AddWithValue("@clo", row["cl"].ToString());
                                statement.ExecuteNonQuery();
                                statement.Dispose();

                            }
                            statement = new SqliteCommand("COMMIT TRANSACTION", mycon); //Close the transaction
                            statement.ExecuteNonQuery();
                            statement.Dispose();
                        }
                    }
                    catch (Exception)
                    {
                        NebliDexNetLog("Error retrieving chart data for market: " + market);
                        return;
                    }
                }

                //Now grab the open orders for the market
                try
                {
                    int page = 0;
                    int order_num = 1;
                    lock (OpenOrderList[market])
                    {
                        OpenOrderList[market].Clear();
                    }
                    while (order_num > 0)
                    {
                        string blockdata = "";
                        lock (dex.blockhandle)
                        {
                            dex.blockhandle.Reset();
                            SendCNServerAction(dex, 50, "" + market + ":" + page); //Get the open orders for the market including IP and port
                                                                                   //Because orders can be very large, divide them per pages of information
                            dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                            if (dex.blockdata == "") { return; }
                            blockdata = dex.blockdata;
                        }
                        JObject js = JObject.Parse(blockdata);
                        if (js["cn.numpts"] != null)
                        {
                            order_num = Convert.ToInt32(js["cn.numpts"].ToString());
                        }
                        else
                        {
                            return; //Someone went wrong with lookup
                        }
                        if (order_num == 0) { break; } //We have all our data
                                                       //Otherwise parse data
                        lock (OpenOrderList[market])
                        {
                            foreach (JToken row in js["cn.result"])
                            {
                                OpenOrder ord = new OpenOrder(); //None of these are mine
                                ord.order_nonce = row["nonce"].ToString();
                                ord.market = Convert.ToInt32(row["mark"].ToString());
                                ord.type = Convert.ToInt32(row["type"].ToString());
                                ord.order_stage = Convert.ToInt32(row["stage"].ToString());
                                if (ord.order_stage == 1)
                                {
                                    ord.pendtime = UTCTime();
                                }
                                ord.cooldownend = Convert.ToUInt32(row["cool"].ToString());
                                ord.price = Decimal.Parse(row["price"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
								ord.original_amount = Decimal.Parse(row["original"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
								ord.amount = Decimal.Parse(row["amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
								ord.minimum_amount = Decimal.Parse(row["min_amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                                ord.ip_address_port[0] = row["ip_add"].ToString();
                                ord.ip_address_port[1] = row["port"].ToString();
                                ord.cn_relayer_ip = row["cn_ip"].ToString();
                                //Before adding, make sure it hasn't been already added via other ways in another thread
                                bool exist = false;
                                for (int i = 0; i < OpenOrderList[market].Count; i++)
                                {
                                    if (OpenOrderList[market][i].order_nonce.Equals(ord.order_nonce) == true)
                                    {
                                        //Already in the list
                                        exist = true; break;
                                    }
                                }
                                if (exist == false)
                                {
                                    OpenOrderList[market].Add(ord);
                                }
                            }
                        }
                        page++; //Go to next page
                    }
                }
                catch (Exception)
                {
                    NebliDexNetLog("Error retrieving market data for market: " + market);
                    return;
                }

                //Then get the recent trade history for all market
                try
                {
                    int numpts = 0;
                    int page = 0;
                    lock (RecentTradeList[market])
                    {
                        RecentTradeList[market].Clear();
                    }
                    do
                    {
                        string blockdata = "";
                        lock (dex.blockhandle)
                        {
                            dex.blockhandle.Reset();
                            SendCNServerAction(dex, 12, market + ":" + page);
                            dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                            if (dex.blockdata == "") { return; }
                            blockdata = dex.blockdata;
                        }

                        JObject js = JObject.Parse(blockdata);
                        if (js["cn.numpts"] != null)
                        {
                            numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                        }
                        else
                        {
                            return; //Someone went wrong with lookup
                        }
                        //Otherwise parse data
                        lock (RecentTradeList[market])
                        {
                            foreach (JToken row in js["cn.result"])
                            {
                                RecentTrade trd = new RecentTrade();
                                trd.utctime = Convert.ToInt32(row["time"].ToString());
                                trd.market = Convert.ToInt32(row["mark"].ToString());
                                trd.type = Convert.ToInt32(row["type"].ToString());
                                trd.price = Convert.ToDecimal(row["price"].ToString(), CultureInfo.InvariantCulture);
                                trd.amount = Convert.ToDecimal(row["amount"].ToString(), CultureInfo.InvariantCulture);
                                RecentTradeList[market].Add(trd); //Most recent trade is first on this list
                            }
                        }
                        page++;
                    } while (numpts > 0);
                }
                catch (Exception)
                {
                    NebliDexNetLog("Unable to acquire most recent trades for this market: " + market);
                    return;
                }
            }

            //Now retrieve list of cooldown traders
            try
            {
                int numpts = 0;
                int page = 0;
                lock (CoolDownList)
                {
                    CoolDownList.Clear();
                }
                do
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    {
                        dex.blockhandle.Reset();
                        SendCNServerAction(dex, 28, "" + page);
                        dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                        if (dex.blockdata == "") { return; }
                        blockdata = dex.blockdata;
                    }

                    JObject js = JObject.Parse(blockdata);
                    if (js["cn.numpts"] != null)
                    {
                        numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                    }
                    else
                    {
                        return; //Someone went wrong with lookup
                    }
                    //Otherwise parse data
                    lock (CoolDownList)
                    {
                        foreach (JToken row in js["cn.result"])
                        {
                            CoolDownTrader trd = new CoolDownTrader();
                            trd.address = row["address"].ToString(); //Address affected
                            trd.cointype = Convert.ToInt32(row["cointype"].ToString()); //Cointype affected
                            trd.utctime = Convert.ToInt32(row["time"].ToString()); //Time when cooldown trader is removed
                            CoolDownList.Add(trd);
                        }
                    }

                    page++;
                } while (numpts > 0);
            }
            catch (Exception)
            {
                NebliDexNetLog("Unable to get full list of cooldown traders");
                return;
            }

            //Now retrieve list of all order requests in the network
            try
            {
                int numpts = 0;
                int page = 0;
                lock (OrderRequestList)
                {
                    OrderRequestList.Clear();
                }
                do
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    {
                        dex.blockhandle.Reset();
                        SendCNServerAction(dex, 30, "" + page);
                        dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                        if (dex.blockdata == "") { return; }
                        blockdata = dex.blockdata;
                    }

                    JObject js = JObject.Parse(blockdata);
                    if (js["cn.numpts"] != null)
                    {
                        numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                    }
                    else
                    {
                        return; //Someone went wrong with lookup
                    }
                    //Otherwise parse data
                    lock (OrderRequestList)
                    {
                        foreach (JToken row in js["cn.result"])
                        {
                            OrderRequest req = new OrderRequest();
                            req.order_nonce_ref = row["request.order_nonce"].ToString();
                            req.request_id = row["request.request_id"].ToString();
                            req.market = Convert.ToInt32(row["request.market"].ToString());
                            req.utctime = Convert.ToInt32(row["request.time"].ToString());
                            req.type = Convert.ToInt32(row["request.type"].ToString());
                            req.order_stage = Convert.ToInt32(row["request.stage"].ToString());
                            req.validator_pubkey = row["request.validator_pubkey"].ToString();
                            req.custodial_add = row["request.custodial_add"].ToString();
                            req.who_validate = Convert.ToInt32(row["request.who_validate"].ToString());

                            req.from_add_1 = row["request.from_add_1"].ToString();
                            req.from_add_2 = row["request.from_add_2"].ToString();
                            req.to_add_1 = row["request.to_add_1"].ToString();
                            req.to_add_2 = row["request.to_add_2"].ToString();
                            req.amount_1 = Convert.ToDecimal(row["request.amount_1"].ToString(), CultureInfo.InvariantCulture);
                            req.amount_2 = Convert.ToDecimal(row["request.amount_2"].ToString(), CultureInfo.InvariantCulture);
                            req.ip_address_taker[0] = row["request.taker_ip"].ToString();
                            req.ip_address_taker[1] = row["request.taker_port"].ToString();
                            req.ip_address_maker[0] = row["request.maker_ip"].ToString();
                            req.ip_address_maker[1] = row["request.maker_port"].ToString();
                            req.maker_cn_ip = row["request.maker_cn_ip"].ToString();
                            req.taker_cn_ip = row["request.taker_cn_ip"].ToString();
                            req.ndex_fee = Convert.ToDecimal(row["request.ndex_fee"].ToString(), CultureInfo.InvariantCulture);
                            OrderRequestList.Add(req);
                        }
                    }

                    page++;
                } while (numpts > 0);
            }
            catch (Exception e)
            {
                NebliDexNetLog("Unable to get full list of order requests for the markets, error: " + e.ToString());
                return;
            }

            //Now retrieve list of all CNs
            //Remove the existing CNs from our list
            lock (CN_Nodes_By_IP)
            {
                NebliDexNetLog("Removing all CN data");
                CN_Nodes_By_IP.Clear();
            }
            GetRemoteCNList(dex);

            //Now ask for the chart last prices for each timeframe
            for (int time = 0; time < 2; time++)
            {
                try
                {
                    int numpts = 0;
                    int page = 0;
                    lock (ChartLastPrice)
                    {
                        ChartLastPrice[time].Clear();
                    }
                    do
                    {
                        string blockdata = "";
                        lock (dex.blockhandle)
                        {
                            dex.blockhandle.Reset();
                            SendCNServerAction(dex, 33, time + ":" + page);
                            dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                            if (dex.blockdata == "") { return; }
                            blockdata = dex.blockdata;
                        }

                        JObject js = JObject.Parse(blockdata);
                        if (js["cn.numpts"] != null)
                        {
                            numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                        }
                        else
                        {
                            return; //Someone went wrong with lookup
                        }
                        //Otherwise parse data
                        lock (ChartLastPrice)
                        {
                            foreach (JToken row in js["cn.result"])
                            {
                                LastPriceObject la = new LastPriceObject();
                                la.market = Convert.ToInt32(row["market"].ToString());
                                la.price = Convert.ToDecimal(row["price"].ToString(), CultureInfo.InvariantCulture);
                                la.atime = Convert.ToInt32(row["atime"].ToString());

                                //First object is oldest
                                ChartLastPrice[time].Add(la);
                            }
                        }

                        page++;
                    } while (numpts > 0);
                }
                catch (Exception)
                {
                    NebliDexNetLog("Unable to get list of last chart prices");
                    return;
                }
            }

            //We don't need to get 24 hour as system will use recent trade data to calculate volume

            //The new CN will not verify each CN balance at once but check random CNs balance occasionally
            //At most 15 minute intervals, also check if CN is online (if not online, will remove from list)
            //We will grab open order information, along with chart data for the market
            if (critical_node_pending == true)
            {
                critical_node_pending = false; //We received all the data
            }
        }

        public static void GetRemoteCNList(DexConnection dex)
        {
            try
            {
                int numpts = 0;
                int page = 0;
                do
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    {
                        dex.blockhandle.Reset();
                        SendCNServerAction(dex, 32, "" + page);
                        dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                        if (dex.blockdata == "") { return; }
                        blockdata = dex.blockdata;
                    }

                    JObject js = JObject.Parse(blockdata);
                    if (js["cn.numpts"] != null)
                    {
                        numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                    }
                    else
                    {
                        return; //Someone went wrong with lookup
                    }
                    //Otherwise parse data
                    lock (CN_Nodes_By_IP)
                    {
                        foreach (JToken row in js["cn.result"])
                        {
                            CriticalNode node = new CriticalNode();
                            node.ip_add = row["cn.ip"].ToString();
                            node.ndex = Convert.ToDecimal(row["cn.ndex"].ToString(), CultureInfo.InvariantCulture);
                            node.pubkey = row["cn.pubkey"].ToString();
                            node.strikes = Convert.ToUInt32(row["cn.strikes"].ToString());
                            node.signature_ip = row["cn.sig"].ToString();
                            node.lastchecked = UTCTime();
                            //Make sure not on local blacklist
                            bool blacklisted = CheckCNBlacklist(node.ip_add);
                            if (CN_Nodes_By_IP.ContainsKey(node.ip_add) == false && blacklisted == false)
                            {
                                CN_Nodes_By_IP[node.ip_add] = node; //Update/add the critical node to our list
                            }
                        }
                    }

                    page++;
                } while (numpts > 0);
            }
            catch (Exception)
            {
                NebliDexNetLog("Unable to get full list of CNs");
            }
            RecalculateCNWeight();
        }

		public static void RecalculateCNWeight()
        {
            //This function will calculate chance of validating next transaction based on total weight online combined with amount of total Cns
            if (critical_node == false) { return; }
            if (cn_ndex_minimum == 0) { return; }
            decimal my_pts = GetWalletAmount(3) / cn_ndex_minimum;
            decimal total_pts = my_pts;
            int total_nodes = 0;
            if (total_pts == 0) { return; }
            string my_ip = getPublicFacingIP();
            lock (CN_Nodes_By_IP)
            {
                foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                {
                    if (cn.signature_ip == null) { return; } //Should not run this function yet
                    if (cn.ip_add.Equals(my_ip) == false)
                    {
                        total_pts += cn.ndex / cn_ndex_minimum;
                    }
                }
                total_nodes = CN_Nodes_By_IP.Count;
            }
            if (total_nodes == 0) { return; }
            decimal inverse_cn = 1m / total_nodes; //Chance of being chosen randomly
            decimal cn_ndex_weight = my_pts / total_pts; //Chance of being chosen based on weight of ndex amount
            my_cn_weight = 0.5m * inverse_cn + 0.5m * cn_ndex_weight;
        }

        public static JObject ValidatorGetInfo(DexConnection con, JObject js)
        {
            //Any CN can confirm the validating information
            string order_nonce = js["cn.order_nonce"].ToString();
            string pubkey = js["cn.validator_pubkey"].ToString();
            string validator_ip = js["cn.validator_ip"].ToString();

            bool proper = false;
            //Find the order request
            OrderRequest req = null;
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_nonce_ref.Equals(order_nonce) == true && OrderRequestList[i].order_stage < 3)
                    {

                        req = OrderRequestList[i];

                        if (OrderRequestList[i].validator_pubkey != pubkey)
                        {
                            return null; //The pubkey doesn't match validator
                        }

                        proper = true;
                        break;
                    }
                }
            }

            if (proper == false) { return null; } //No matching order request

            //Otherwise return data about the order
            int maker = Convert.ToInt32(js["cn.is_maker"].ToString());
            int getinfo = Convert.ToInt32(js["cn.getinfo_only"].ToString()); //If get info only, do not connect rsa to order or create session key
            JObject vjs = new JObject();
            vjs["cn.method"] = "cn.validatorgetinfo";
            vjs["cn.response"] = 1;
            vjs["cn.order_nonce"] = order_nonce;
            vjs["trade.reqtime"] = req.utctime;
            string validator_sig = "";
            lock (CN_Nodes_By_IP)
            {
                foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                {
                    if (cn.ip_add.Equals(validator_ip) == true)
                    {
                        validator_sig = cn.signature_ip; //Signature of IP
                        break;
                    }
                }
            }

            vjs["cn.validator_sig"] = validator_sig;
            vjs["trade.ndex_fee"] = req.ndex_fee.ToString(CultureInfo.InvariantCulture);

            if (req.type == 0)
            {
                //Taker is buying
                vjs["trade.amount"] = req.amount_2.ToString(CultureInfo.InvariantCulture); //This is the trade amount requested by taker
            }
            else
            {
                //Taker is selling
                vjs["trade.amount"] = req.amount_1.ToString(CultureInfo.InvariantCulture); //This is the trade amount taker is sending to maker
            }

            if (getinfo == 0)
            {
                //This is a connecting trader node for validation
                con.rsa_pubkey = js["trade.rsa_pubkey"].ToString();
                con.contype = 4; //Validation connection
                con.tn_connection_nonce = order_nonce;
                con.tn_connection_time = req.utctime;

                con.aes_key = GenerateHexNonce(32);
                vjs["cn.sessionkey"] = EncryptRSAText(con.aes_key, con.rsa_pubkey);
                lock (transactionLock)
                {
                    //Prevent more than one thread from using the network
                    Network my_net;
                    if (testnet_mode == false)
                    {
                        my_net = Network.Main;
                    }
                    else
                    {
                        my_net = Network.TestNet;
                    }

                    //Change the network
                    ChangeVersionByte(0, ref my_net); //NEBL network
                    Wallet neblwall = null;
                    for (int i = 0; i < WalletList.Count; i++)
                    {
                        if (WalletList[i].type == 0)
                        {
                            //Neblio wallet
                            neblwall = WalletList[i]; break;
                        }
                    }
                    ExtKey priv_key = ExtKey.Parse(neblwall.private_key, my_net);
                    vjs["cn.sessionkey_sig"] = priv_key.PrivateKey.SignMessage(vjs["cn.sessionkey"].ToString()); //signature of encrypted session key
                }

                if (maker == 1)
                {
                    con.tn_is_maker = true;
                }
                else
                {
                    con.tn_is_maker = false;
                }
            }

            if (maker == 1)
            {
                //Maker is requesting information
                vjs["trade.recipient_add"] = req.to_add_2;
            }
            else
            {
                //Taker requesting info
                //Going to maker
                vjs["trade.recipient_add"] = req.to_add_1;
            }

            return vjs;
        }

        public static JObject GetCNValidator(DexConnection con, JObject js)
        {
            string order_nonce = js["cn.order_nonce"].ToString();

            bool proper = false;
            string maker_ip = "";
            string taker_ip = "";

            //Find the order request
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_nonce_ref.Equals(order_nonce) == true && OrderRequestList[i].order_stage < 3)
                    {

                        maker_ip = OrderRequestList[i].ip_address_maker[0]; //These IPs may not match the order request IP if on local network so unreliable
                        taker_ip = OrderRequestList[i].ip_address_taker[0];

                        proper = true;
                    }
                }
            }

            if (proper == false) { return null; } //No matching order request

            JObject vjs = new JObject();
            vjs["cn.method"] = "cn.relayvalidator";
            vjs["cn.response"] = 0;
            vjs["cn.order_nonce"] = order_nonce;

            string my_ip = getPublicFacingIP();
            string my_pubkey = GetWalletPubkey(3);

            vjs["cn.initiating_cn_ip"] = my_ip; //The IP address of the CN that found the validation node

            if (CN_Nodes_By_IP.Count < 2 || cn_ndex_minimum == 0)
            {
                //There is only 1 critical node, this cn has to be the validator
                vjs["cn.validator_ip"] = my_ip;
                vjs["cn.validator_pubkey"] = my_pubkey;
                return vjs;
            }

            decimal total_pts = 0;

            //Now find a validator
            //Our validator cannot be this CN, maker IP or Taker IP

            List<CriticalNode> CN_List = new List<CriticalNode>();
            lock (CN_Nodes_By_IP)
            {
                foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                {
                    if (cn.ip_add.Equals(my_ip) == false && cn.ip_add.Equals(maker_ip) == false && cn.ip_add.Equals(taker_ip) == false)
                    {
                        total_pts += cn.ndex / cn_ndex_minimum;
                        CN_List.Add(cn);
                    }
                }
            }

            //If there are not enough choices, then we can choose maker and taker IP
            if (CN_List.Count == 0)
            {
                lock (CN_Nodes_By_IP)
                {
                    foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                    {
                        if (cn.ip_add.Equals(my_ip) == false)
                        {
                            total_pts += cn.ndex / cn_ndex_minimum;
                            CN_List.Add(cn);
                        }
                    }
                }
            }
            
            //Make choosing a validator fairer for those who do not have many NDEX
            bool equal_chance = false;
            int coin_flip = (int)Math.Round(GetRandomNumber(0, 1));
            if (coin_flip == 0)
            {
                //There is a 50% chance of choosing critical nodes based on only the amount of nodes online
                //And a 50% chance of choosing critical nodes based on weighted ndex of those nodes
                equal_chance = true;
                NebliDexNetLog("Validator being chosen based on equal chance formula");
            }

            CriticalNode validator = null;
            while (validator == null)
            {

                decimal end_pt = GetRandomDecimalNumber(0, total_pts);
                for (int i = 0; i < CN_List.Count; i++)
                {
                    if (CN_List[i].ip_add.Equals(my_ip) == false)
                    {
                        decimal cn_pts = CN_List[i].ndex / cn_ndex_minimum;
                        end_pt -= cn_pts;
                        if (end_pt <= 0)
                        {
                            //This is our validating CN (Simple algorithm)
                            validator = CN_List[i];
                            break;
                        }
                    }
                }

                if (equal_chance == true)
                {
					if (CN_List.Count == 0) { break; } //Must be at least 1 CN
                    int node_num = (int)Math.Round(GetRandomNumber(1, CN_List.Count)) - 1;
                    validator = CN_List[node_num];
                }

                if (validator == null) { break; }

                //Now try to connect and then ask if order_nonce is recognized for order request
                try
                {
                    IPAddress address = IPAddress.Parse(validator.ip_add);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, critical_node_port, 5))
                    {
                        NebliDexNetLog("Validating Node: Connected to: " + validator.ip_add);
                        //Connect to the critical node and get a list of possible critical nodes
                        client.ReceiveTimeout = 5000; //5 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        JObject vob = new JObject();
                        vob["cn.method"] = "cn.orderrequestexist";
                        vob["cn.response"] = 0;
                        vob["cn.order_nonce"] = order_nonce;
                        vob["cn.server_minversion"] = protocol_min_version; //Send a min_version byte to server so that it knows if it can accept request

                        string json_encoded = JsonConvert.SerializeObject(vob);
                        string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                        //Convert it to a JSON
                        //The other critical node will return a response before relaying to other nodes
                        //Thus freeing up this node to do stuff
                        JObject result = JObject.Parse(response);
                        if (result["cn.result"].ToString() != "Order Request Exists")
                        {
                            CN_List.Remove(validator);
                            total_pts -= validator.ndex / cn_ndex_minimum; //Take it away from the calculation and start over
                            validator = null;
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
                    if (validator != null)
                    {
                        NebliDexNetLog("Validating Node Error: " + validator.ip_add + " error: " + e.ToString());
                        CN_List.Remove(validator);
                        total_pts -= validator.ndex / cn_ndex_minimum; //Take it away from the calculation and start over
                    }
                    validator = null; //Restart
                }

                if (CN_List.Count == 0) { break; }

            }

            if (validator == null)
            {
                //No other node is available
                if (CN_Nodes_By_IP.Count <= 2)
                {
                    //There is only one available critical node
                    vjs["cn.validator_ip"] = my_ip;
                    vjs["cn.validator_pubkey"] = my_pubkey;
                    return vjs;
                }
                else
                {
                    NebliDexNetLog("No validation node available");
                    return null; //This node cannot be a validator unless only node on network
                }
            }
            else
            {
                vjs["cn.validator_ip"] = validator.ip_add;
                vjs["cn.validator_pubkey"] = validator.pubkey;
            }

            return vjs;
        }

        public static JObject CreateJSONTradeComplete(string nonce)
        {
            //Mark the order request closed and relay the message to all the CNs
            //Recent trade history
            JObject relayjs = new JObject();
            relayjs["cn.method"] = "cn.relaycancelrequest";
            relayjs["cn.response"] = 0;
            relayjs["cn.order_nonce"] = nonce;
            relayjs["trade.success"] = 1; //This is presuming that the taker funds the account
            relayjs["trade.complete_time"] = UTCTime(); //This will be used to sync trades among network
            relayjs["relay.nonce"] = GenerateHexNonce(24);
            relayjs["relay.timestamp"] = UTCTime();
            CheckMessageRelay(relayjs["relay.nonce"].ToString(), Convert.ToInt32(relayjs["relay.timestamp"].ToString()));

            return relayjs;
        }

        public static void SendPostTradeAction(int action, int reqtime, string nonce)
        {
            //This will send the connected validation nodes information about the trade
            if (action == 0)
            {
                //Trade failed, maker tx not confirmed, alert taker to retrieve funds
                JObject vjs = new JObject();
                vjs["cn.method"] = "cn.validator_taker_canceltrade";
                vjs["cn.response"] = 0;
                vjs["cn.order_nonce"] = nonce;
                vjs["cn.order_utctime"] = reqtime; //These values are used to query other auditors to make sure they have redeem script
                vjs["cn.extra_info"] = "The maker rejected this trade request";

                //These go directly to the connected nodes
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 4)
                        {
                            //Only send this message to taker
                            if (DexConnectionList[i].tn_connection_nonce == nonce && DexConnectionList[i].tn_is_maker == false && DexConnectionList[i].tn_connection_time == reqtime)
                            {
                                SendCNServerAction(DexConnectionList[i], 46, JsonConvert.SerializeObject(vjs));
                            }
                        }
                    }
                }

            }
            else if (action == 1)
            {
                //Trade failed, alert maker to cancel trade on their end
                JObject vjs = new JObject();
                vjs["cn.method"] = "cn.validator_maker_canceltrade";
                vjs["cn.response"] = 0;
                vjs["cn.order_nonce"] = nonce;
                vjs["cn.order_utctime"] = reqtime; //These values are used to query other auditors to make sure they have redeem script
                vjs["cn.extra_info"] = "The order taker failed to fund the contract in time.";

                //These go directly to the connected nodes
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 4)
                        {
                            //Only send this message to taker
                            if (DexConnectionList[i].tn_connection_nonce == nonce && DexConnectionList[i].tn_is_maker == true && DexConnectionList[i].tn_connection_time == reqtime)
                            {
                                SendCNServerAction(DexConnectionList[i], 46, JsonConvert.SerializeObject(vjs));
                            }
                        }
                    }
                }
            }
        }

        public static void CheckRandomCNBalance()
        {
            //This function will check a random CN signature, balance and see if its connectable
            //If it fails any of the checks, it is removed

            if (ntp1downcounter > 1) { return; } //Do not try to check a CN while the API server is down

            string my_ip = getPublicFacingIP();
            CriticalNode sel_node = null;
            lock (CN_Nodes_By_IP)
            {
                int pos = (int)Math.Round(GetRandomNumber(1, CN_Nodes_By_IP.Count)) - 1;
                foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                {
                    if (cn.ip_add.Equals(my_ip) == false)
                    {
                        if (pos == 0)
                        {
                            sel_node = cn; break;
                        }
                    }
                    pos--;
                }
            }
            if (sel_node == null) { return; } //No CN to check
            if (UTCTime() - sel_node.lastchecked < 60 * 15) { return; } //Too soon to check again

            //Otherwise check the sig
            bool remove_it = false;

            if (sel_node.signature_ip == null)
            {
                NebliDexNetLog("Removing CN :" + sel_node.ip_add + " because no signature present");
                remove_it = true;
            }

            try
            {
                //Now verify if the message matches the sig using the pubkey
                if (remove_it == false)
                {
                    PubKey pub = new PubKey(sel_node.pubkey);
                    bool check = pub.VerifyMessage(sel_node.ip_add, sel_node.signature_ip);
                    if (check == false)
                    {
                        //Message was altered
                        remove_it = true;
                    }

                    if (remove_it == false)
                    {
                        //Generate a NEBL address from the public key
                        string ndex_add;
                        lock (transactionLock)
                        {
                            Network my_net;
                            if (testnet_mode == false)
                            {
                                my_net = Network.Main;
                            }
                            else
                            {
                                my_net = Network.TestNet;
                            }
                            ChangeVersionByte(3, ref my_net);
                            ndex_add = pub.GetAddress(my_net).ToString();
                        }

                        //Now verify the ndex balance using ntp1node
                        decimal balance = GetBlockchainAddressBalance(3, ndex_add, false);
                        if (balance < cn_ndex_minimum)
                        {
                            //Not enough balance to be a CN

                            //Make sure our internet connection is working
                            JArray utxo = GetAddressUnspentTX(null, 3, GetWalletAddress(3));
                            if (utxo == null)
                            {
                                NebliDexNetLog("Internet is likely down right now");
                                cn_network_down = true; //This will force a redownload of all CN data
                                return;
                            }
                            remove_it = true;
                        }
                        sel_node.ndex = balance; //Update the balance
                    }
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Failed to verify CN signature, error: " + e.ToString());
                remove_it = true;
            }

            if (remove_it == false)
            {
                //Now check to see if server is available at address
                bool connected = false;
                try
                {
                    IPAddress address = IPAddress.Parse(sel_node.ip_add);
                    int port = critical_node_port;
                    TcpClient client = new TcpClient();
                    //This will wait 30 seconds before moving on (bad connection)
                    if (ConnectSync(client, address, port, 30))
                    {
                        client.ReceiveTimeout = 30000; //30 seconds
                        NetworkStream nStream = client.GetStream(); //Get the stream

                        JObject vob = new JObject();
                        vob["cn.method"] = "cn.getversion";
                        vob["cn.response"] = 0;
                        vob["cn.authlevel"] = 1;

                        string json_encoded = JsonConvert.SerializeObject(vob);
                        string response = CNWaitResponse(nStream, client, json_encoded);

                        NebliDexNetLog("Verify CN Connection Response for CN (" + sel_node.ip_add + "): " + response);

                        if (response.Length > 0)
                        {
                            JObject jresp = JObject.Parse(response);
                            string result = jresp["cn.result"].ToString();
                            if (result.Equals("Not a CN") == false)
                            {
                                //We want to be a CN
                                int cversion = Convert.ToInt32(result);
                                if (cversion >= protocol_min_version)
                                {
                                    //If this CN is less than our minimum, remove it from our list
                                    //Connection was successful
                                    connected = true;
                                }
                            }
                            else
                            {
                                //Not detected as a CN, rebroadcast our status
                                //Try to reconnect the CN
                                reconnect_cn = true;
                                connected = true; //Stay connected for time being
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
                    NebliDexNetLog("Unable to connect to the CN: " + sel_node.ip_add + ", error: " + e.ToString());

                    //Make sure our internet connection is working
                    JArray utxo = GetAddressUnspentTX(null, 3, GetWalletAddress(3));
                    if (utxo == null)
                    {
                        NebliDexNetLog("Internet is likely down right now");
                        cn_network_down = true;
                        return;
                    }
                }

                if (connected == false)
                {
                    remove_it = true;
                }
            }

            if (remove_it == true)
            {
                //Remove any order linked to this CN IP address about to be deleted
                for (int market = 0; market < total_markets; market++)
                {
                    lock (OpenOrderList[market])
                    {
                        for (int pos = OpenOrderList[market].Count - 1; pos >= 0; pos--)
                        {
                            if (OpenOrderList[market][pos].cn_relayer_ip.Equals(sel_node.ip_add) == true)
                            {
                                OpenOrderList[market][pos].deletequeue = true;
                            }
                        }
                    }
                }

                NebliDexNetLog("Removing bad connection to CN: " + sel_node.ip_add);
                lock (CN_Nodes_By_IP)
                {
                    CN_Nodes_By_IP.Remove(sel_node.ip_add); //Take this node out of the running
                }
				RecalculateCNWeight();
                RecreateCNList();
            }
            else
            {
                sel_node.lastchecked = UTCTime(); //CN is online and working, up to date
            }

        }

        public static void AddIPToCNBlacklist(string ip)
        {
            //This will add an IP address to a blacklist that will last for 10 days
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            //Add certain values to the database
            string myquery = "Insert Into BLACKLIST (utctime, type, value) Values (@time, 0, @ip);";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@time", UTCTime());
            statement.Parameters.AddWithValue("@ip", ip);
            statement.ExecuteNonQuery();
            statement.Dispose();

            mycon.Close();
        }

        public static bool CheckCNBlacklist(string ip)
        {
            SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App.App_Path + "/data/neblidex.db\";Version=3;");
            mycon.Open();

            //Set our busy timeout, so we wait if there are locks present
            SqliteCommand statement = new SqliteCommand("PRAGMA busy_timeout = 5000", mycon); //Create a transaction to make inserts faster
            statement.ExecuteNonQuery();
            statement.Dispose();

            string myquery = "Select nindex From BLACKLIST Where value = @ip And type = 0";
            statement = new SqliteCommand(myquery, mycon);
            statement.Parameters.AddWithValue("@ip", ip);
            SqliteDataReader statement_reader = statement.ExecuteReader();
            bool dataavail = statement_reader.Read();
            statement_reader.Close();
            statement.Dispose();
            mycon.Close();
            return dataavail; //False = not on blacklist    lo      
        }

        //Atomic Functions
        public static JObject CNValidateTxFees(JObject tx, DexConnection con, bool from_maker)
        {
            //This function gets and verifies the transaction hash with points on the blockchain
            //All this function does is verify that fees are paid to it
            //It doesn't check anything else, it delegates that job to the trader nodes themselves
            string order_nonce = tx["cn.order_nonce"].ToString();
            OrderRequest req = null;
			bool potentially_stale = true;
			int stale_trade_time = 0;
            lock (OrderRequestList)
            {
                for (int i = 0; i < OrderRequestList.Count; i++)
                {
                    if (OrderRequestList[i].order_nonce_ref.Equals(order_nonce) == true && OrderRequestList[i].order_stage > 0)
                    {
                        if (OrderRequestList[i].order_stage == 3)
                        { //Closed already
							potentially_stale = true;
							stale_trade_time = OrderRequestList[i].utctime;
							continue; //Continue to look for a non-stale trade
                        }
                        req = OrderRequestList[i]; break;
                    }
                }
            }

            if (from_maker == false)
            {
                NebliDexNetLog("Validating Trading Fees From Taker: " + order_nonce);
            }
            else
            {
                NebliDexNetLog("Validating Trading Fees From Maker: " + order_nonce);
            }

            if (req == null) { 
				if(potentially_stale == true){
					//The validator found a matching order but they are all from old trades, let the trader know
					//We will reject with this stage as well
                    NebliDexNetLog("Trade already closed, sending notice to trader: " + order_nonce);
                    if (from_maker == false)
                    {
						SendPostTradeAction(0, stale_trade_time, order_nonce); //Tell taker trade is already canceled
                    }
                    else
                    {
						SendPostTradeAction(1, stale_trade_time, order_nonce); //Tell maker trade is already canceled
                    }
				}
				return null; 
			}

            string my_add = GetWalletAddress(3); //NDEX Wallet is how we get paid
            string feefrom_add = tx["trade.ndex_add"].ToString(); //The NDEX wallet where the fee is originating

            Decimal expected_fee = req.ndex_fee / 2m;
            bool skip_fee_calc = false;
            int trade_wallet = MarketList[req.market].trade_wallet;
            int base_wallet = MarketList[req.market].base_wallet;
            if (req.type == 0)
            {
                //Taker is buying
                if (trade_wallet == 3)
                {
                    //Buying NDEX
                    if (from_maker == false)
                    {
                        //This is the taker's fee transaction. Since no fee is required, skip fee analysis
                        skip_fee_calc = true;
                    }
                    else
                    {
                        expected_fee = req.ndex_fee; //We expect the maker to cover the entire fee
                    }
                }
            }
            else
            {
                //Taker is selling
                if (trade_wallet == 3)
                {
                    //Selling NDEX
                    if (from_maker == false)
                    {
                        expected_fee = req.ndex_fee;
                    }
                    else
                    {
                        skip_fee_calc = true; //Maker will typically not even send a transaction in this case
                    }
                }
            }

            string tx_hex = AESDecrypt(tx["trade.validator_mytx"].ToString(), con.aes_key);
            string tx_fee_hex = AESDecrypt(tx["trade.validator_feetx"].ToString(), con.aes_key);

            if (skip_fee_calc == false)
            {

                string eval_tx = tx_hex;

                if (tx_fee_hex.Length > 0)
                {
                    eval_tx = tx_fee_hex;
                }

                try
                {
                    Transaction tx_bin = new Transaction(eval_tx, true); //It will decode the neblio based transaction

                    //Make sure the balance is there to support the trade
                    Decimal chain_bal = GetBlockchainAddressBalance(3, feefrom_add, false);
                    if (chain_bal < expected_fee)
                    {
                        //The account doesn't have the balance to send this transaction
                        NebliDexNetLog("Unable determine blockchain balance for address or balance too low: " + feefrom_add);
                        return null;
                    }

                    //Get the unspent outputs for this feefrom_add from network
                    JArray utxo = GetAddressUnspentTX(null, 3, feefrom_add);
                    if (utxo == null)
                    {
                        NebliDexNetLog("Unable determine UTXO: " + feefrom_add);
                        return null;
                    }

                    int txin_count = tx_bin.Inputs.Count;
                    int txout_count = tx_bin.Outputs.Count;
                    for (int i = 0; i < txin_count; i++)
                    {
                        //All txins here need to be acquired from the unspent
                        TxIn input = tx_bin.Inputs[i];
                        if (input.ScriptSig.IsValid == false)
                        {
                            NebliDexNetLog("ScriptSig is invalid for transaction");
                            return null;
                        }
                        bool utxo_exist = false;
                        //Go through each row of results
                        foreach (JToken row in utxo)
                        {
                            string hash = row["tx_hash"].ToString();
                            uint pos = Convert.ToUInt32(row["tx_pos"].ToString());
                            if (hash.Equals(input.PrevOut.Hash.ToString()) == true && pos == input.PrevOut.N)
                            {
                                utxo_exist = true; break; //This is unspent, good
                            }
                        }

                        if (utxo_exist == false)
                        {
                            NebliDexNetLog("Unable to find this UTXO in available UTXOs: " + input.PrevOut.Hash.ToString());
                            return null;
                        }
                    }

                    int validator_outputnum = 0;
                    string op_return_info = "";
                    lock (transactionLock)
                    {
                        Network my_net;
                        if (testnet_mode == false)
                        {
                            my_net = Network.Main;
                        }
                        else
                        {
                            my_net = Network.TestNet;
                        }
                        ChangeVersionByte(3, ref my_net);
                        for (int i = 0; i < txout_count; i++)
                        {
                            TxOut output = tx_bin.Outputs[i];
                            Script srpt = output.ScriptPubKey;
                            if (srpt.IsUnspendable == false)
                            {
                                //Must be spendable
                                string output_add = srpt.GetDestinationAddress(my_net).ToString();
                                if (output_add.Equals(my_add) == true)
                                {
                                    //Found my address in transaction
                                    //Mark the location of this output, we will compare it against the NTP1 instructions located in the OP_RETURN
                                    validator_outputnum = i;
                                }
                            }
                            else
                            {
                                //This is a OP_RETURN script, extract the pushdata
                                string[] op_return_array = srpt.ToString().Split(' ');
                                op_return_info = op_return_array[1];
                            }
                        }
                    }

                    if (op_return_info.Length == 0)
                    {
                        NebliDexNetLog("No OP_RETURN data for transaction");
                        return null;
                    }

                    List<NTP1Instructions> ti_list = _NTP1ParseScript(op_return_info);
                    if (ti_list == null)
                    {
                        NebliDexNetLog("Failure to parse this script");
                        return null;
                    }

                    if (ti_list.Count < 1)
                    {
                        NebliDexNetLog("No transfer instructions found in script");
                        return null;
                    }

                    bool verified = false;
                    for (int i = 0; i < ti_list.Count; i++)
                    {
                        //Go through the transfer instructions and make sure that there is a fee paid to our output
                        if (ti_list[i].vout_num == validator_outputnum)
                        {
                            if (ti_list[i].amount == Convert.ToUInt64(expected_fee))
                            {
                                verified = true;
                            }
                            break;
                        }
                    }

                    if (verified == false)
                    {
                        NebliDexNetLog("Unable to find fee paid to validator");
                        return null;
                    }

                }
                catch (Exception e)
                {
                    NebliDexNetLog("Transaction Read Error: " + e.ToString());
                    return null;
                }
            }
            else
            {
                //May skip fee validation if no fee required
                NebliDexNetLog("No fee was required");
            }

            //After we validate we will relay across the network to pertinent party

            if (from_maker == false)
            {
                string my_ip = getPublicFacingIP();
                string my_pubkey = "";

                lock (CN_Nodes_By_IP)
                {
                    foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                    {
                        if (cn.ip_add.Equals(my_ip) == true)
                        {
                            my_ip = cn.ip_add;
                            my_pubkey = cn.pubkey;
                            break;
                        }
                    }
                }

                //Make sure there is not already a database entry for this request
                if (GetValidationData("validating_cn_pubkey", req.utctime, req.order_nonce_ref).Length > 0)
                {
                    return null;
                }

                string redeemscript_add = tx["trade.contract_add"].ToString();

				//If taker is sending ethereum
                bool taker_sending_eth = false;
                if (req.type == 0)
                {
                    //Taker buying
                    if (GetWalletBlockchainType(MarketList[req.market].base_wallet) == 6)
                    {
                        //Sending eth, only possible if eth is base market wallet
                        taker_sending_eth = true;
                    }
                }
                else
                {
                    //Taker selling
                    if (GetWalletBlockchainType(MarketList[req.market].trade_wallet) == 6)
                    {
                        //Sending eth
                        taker_sending_eth = true;
                    }
                }

                JObject databasejs = new JObject();
                databasejs["utctime"] = req.utctime;
                databasejs["nonce"] = req.order_nonce_ref;
                if (taker_sending_eth == false)
                {
                    databasejs["redeemscript_add"] = redeemscript_add;
                }
                else
                {
                    databasejs["redeemscript_add"] = tx["trade.secret_hash"]; //Store the hash of the secret for transaction monitoring
                }
                databasejs["cn_pubkey"] = my_pubkey;
                databasejs["market"] = req.market;
                databasejs["type"] = req.type;
                databasejs["taker_feetx"] = tx_fee_hex;
                databasejs["taker_tx"] = tx_hex;
                AddValidatingTransaction(databasejs); //Add a database entry with this information

                //Taker has given us the fee transaction and its normal transaction, don't broadcast yet
                //Obtain maker fee transaction and acknowledgement
                JObject vjs = new JObject();
                vjs["cn.method"] = "cn.validator_notifymaker";
                vjs["cn.response"] = 0;
                vjs["cn.order_nonce"] = order_nonce;
                vjs["cn.validator_ip"] = my_ip;
                vjs["cn.validator_pubkey"] = my_pubkey;
                vjs["cn.initiating_cn_ip"] = tx["cn.initiating_cn_ip"];
                //Also send the maker information about the taker contract
                vjs["trade.taker_unlock_time"] = tx["trade.unlock_time"]; //Used to recreate the contract
                vjs["trade.taker_contract_add"] = redeemscript_add;
                vjs["trade.taker_send_add"] = tx["trade.taker_send_add"]; //The address that the taker funds are coming from
                vjs["trade.secret_hash"] = tx["trade.secret_hash"]; //Hash of the secret. Cannot obtain secret from hash

                NebliDexNetLog("Finished validating Taker Transaction: " + order_nonce);
                return vjs;
            }
            else
            {

                //Maker has sent its fee and relevant info
                JObject vjs = new JObject();
                //Double check that taker is ok with trade so far
                vjs["cn.method"] = "cn.validator_takerconfirm";
                vjs["cn.response"] = 0;
                vjs["cn.order_nonce"] = order_nonce;
                vjs["cn.reqtime"] = req.utctime;
                vjs["cn.maker_contract_add"] = tx["trade.contract_add"];
                vjs["trade.maker_send_add"] = tx["trade.maker_send_add"]; //Taker will use the following information to verify maker contract

                DexConnection taker_con = null;
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].outgoing == false && DexConnectionList[i].contype == 4 && DexConnectionList[i].open == true)
                        {
                            //Send to only the taker
                            if (DexConnectionList[i].tn_connection_nonce == req.order_nonce_ref)
                            {
                                if (DexConnectionList[i].tn_is_maker == false)
                                {
                                    taker_con = DexConnectionList[i]; break;
                                }
                            }
                        }
                    }
                }

                if (taker_con == null)
                {
                    NebliDexNetLog("Could not locate Taker connection");
                    return null;
                }

                string blockdata = "";
                lock (taker_con.blockhandle)
                {
                    taker_con.blockdata = "";
                    taker_con.blockhandle.Reset();
                    SendCNServerAction(taker_con, 46, JsonConvert.SerializeObject(vjs));
                    taker_con.blockhandle.WaitOne(15000); //This will wait 15 seconds for a response                                
                    if (taker_con.blockdata == "")
                    {
                        NebliDexNetLog("Taker failed to response to confirm trade");
                        return null;
                    }
                    blockdata = taker_con.blockdata;
                }

                if (blockdata != "Taker Accepts")
                {
                    NebliDexNetLog("Taker rejected maker contract address");
                    return null;
                }

                //Now broadcast the stored transactions and sit back and relax
                string taker_tx = GetValidationData("taker_tx", req.utctime, req.order_nonce_ref);
                string taker_feetx = GetValidationData("taker_feetx", req.utctime, req.order_nonce_ref);
                string maker_feetx = tx_fee_hex; //Get the decrypted maker hex
                                                 //Remove the taker information from the validator
                                                 //Remove taker information
                SetValidationData("taker_tx", "", req.utctime, req.order_nonce_ref);
                SetValidationData("taker_feetx", "", req.utctime, req.order_nonce_ref);

                int taker_sendwallet = 0;
                if (req.type == 0)
                {
                    //Taker buying with base
                    taker_sendwallet = MarketList[req.market].base_wallet;
                }
                else
                {
                    //Taker selling
                    taker_sendwallet = MarketList[req.market].trade_wallet;
                }

                if (taker_tx.Length == 0)
                {
                    //This is possible if the trade is canceled early by maker
					NebliDexNetLog("Trade was canceled early by maker before broadcasting");
                    return null;
                }

                if (taker_feetx.Length > 0)
                {
                    bool timeout;
                    string txid = TransactionBroadcast(3, taker_feetx, out timeout);
                    if (txid.Length == 0)
                    {
                        NebliDexNetLog("Taker fee transation has failed to post");
                        //Failed posting
                        return null;
                    }
                }

                //Now send the taker transaction
                bool timeout2;
                string txid2 = TransactionBroadcast(taker_sendwallet, taker_tx, out timeout2);
                if (txid2.Length == 0)
                {
                    //Failed to post taker transaction into scripthash
                    NebliDexNetLog("Taker transation has failed to post");
                    return null;
                }

                //Taker transaction has posted, now 
                if (maker_feetx.Length > 0)
                {
                    bool timeout;
                    string txid = TransactionBroadcast(3, maker_feetx, out timeout);
                    if (txid.Length == 0)
                    {
                        NebliDexNetLog("Maker fee transaction failed to post");
                        //Failed posting
                        return null;
                    }
                }

                SetValidationData("status", 1, req.utctime, req.order_nonce_ref); //Uncancelable at this point

                //Claim the CN fee now as we have done our job (cost of transmitting data)
                AddMyCNFee(req.market, req.ndex_fee);

                //Now send the message of trade complete to all nodes, this will close all the order requests
                JObject complete_msg = CreateJSONTradeComplete(order_nonce);
                RelayCloseOrderRequest(con, complete_msg);

                NebliDexNetLog("Finished validating Maker transaction and taker confirms it: " + order_nonce);
                NebliDexNetLog("Posting completed trade: " + order_nonce);
                return null;
            }
        }
		
	}
	
}