/*
 * Created by SharpDevelop.
 * User: David
 * Date: 2/23/2018
 * Time: 3:05 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

using System;
using Gtk;
using System.Data;
using NBitcoin;
using Nethereum;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
		public static string NEBLAPI_server; //URL of the Neblio API (used by default but not required)
		
		public static List<DexConnection> DexConnectionList = new List<DexConnection>();
		public static AsyncCallback DexConnCallback = new AsyncCallback(DexConnectionCallback);
		
		public class DexConnection
		{
			//This is a connection to another computer
			public string[] ip_address = new string[2]; //Port is the second string
			public bool outgoing;
			public bool open;
			public int version; //The version of the client on the other side
			public int contype; //Connection type
			//0 - Neblio electrum
			//1 - Electrum node
            //2 - Not used anymore
			//3 - CN connection
			//4 - Validation connection
			public int blockchain_type;
            //1 - Bitcoin based
            //2 - Litecoin based
            //3 - Groestlcoin based
            //4 - Bitcoin Cash (ABC) based
            //5 - Monacoin based
			
			public TcpClient client;
			public NetworkStream stream;
			public SslStream secure_stream = null; //Used for all electrum servers
			public Byte[] buf = new Byte[256]; //For the async connection
			
			public int lasttouchtime; //UTCTime of last activity
			public uint electrum_id;
			public AutoResetEvent blockhandle = new AutoResetEvent(false); //Flag to wait for blocking data
			public string blockdata; //The data received after blocking call
			
			public string rsa_pubkey; //The public key associated with this connection
			public string aes_key; //Symmetric key for communication from connection
			public string tn_connection_nonce; //The nonce of the order associated with this connection
			public int tn_connection_time; //The time associated with the connection nonce (for taker only)
			public bool tn_is_maker;
			public int msg_count=0; //This is used to throttle the amount of messages received from a normal connection over 5 second period
			
			public DexConnection(){
				lasttouchtime = UTCTime();
				electrum_id=0;
				version = 0;
				tn_connection_nonce = "";
				tn_is_maker=false;
			}
			
			public void closeConnection(){
				lock(DexConnectionReqList){
					for(int i=DexConnectionReqList.Count-1;i >= 0;i--){
						if(DexConnectionReqList[i].electrum_con == this){
							DexConnectionReqList[i].delete = true;
						}
					}
				}
				open = false;
				try {
					stream.Close();
					if (secure_stream != null)
                    {
                        secure_stream.Close();
                    }
					client.Close();	
				} catch (Exception e) {
					NebliDexNetLog("Error closing connection, may already be closed: "+e.ToString());
				}
			}
		}
		
		//DexConnection requests, will be matched to the response (mostly for electrum)
		//It will find the dexconnection request and match it to the electrum ID, then remove the request
		public static List<DexConnectionReq> DexConnectionReqList = new List<DexConnectionReq>();

		public class DexConnectionReq
		{
			public DexConnection electrum_con;
			public int requesttype;
			public uint electrum_id;
			public int creation_time;
			public bool delete; //This is a flag to remove the object from list
		}

		//Create a list of all the DNSSeeds for all electrum nodes
        public static List<DNSSeed> DNSSeedList = new List<DNSSeed>();

        //New class that represents DNS seed and type
        public class DNSSeed
        {
            public string domain;
            public int port;
            public bool ssl;
            public int cointype;
            //Possible cointypes are:
            //0 - Neblio based (including tokens)
            //1 - Bitcoin based
            //2 - Litecoin based
            //3 - Groestlcoin based
            //4 - Bitcoin Cash (ABC) based
            //5 - Monacoin based
            public DNSSeed(string d, int p, bool s, int ct)
            {
                domain = d;
                port = p;
                ssl = s;
                cointype = ct;
            }
        }
		
		public static void AddDNSServers()
        {
            //This is a preprogrammed list of all the potential DNS servers
            if (DNSSeedList.Count > 0) { return; } //No need to do this more than once

            //NEBL Electrum DNS list
            if (testnet_mode == false)
            {
                //We are converting Neblio to NTP1 node system
                //It uses HTTPS protocol
                NEBLAPI_server = "https://ntp1node.nebl.io"; //Critical Nodes server as backup to API server going down

                //Bitcoin Electrum DNS Seeds
                //Switching over to secure SSL
                DNSSeedList.Add(new DNSSeed("fortress.qtornado.com", 50002, true, 1)); //Adding a Bitcoin Seed at port 50002
                DNSSeedList.Add(new DNSSeed("aspinall.io", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("electrumx-core.1209k.com", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("electrum.hodlister.co", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("btc.theblains.org", 50006, true, 1));
                DNSSeedList.Add(new DNSSeed("kirsche.emzy.de", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("e1.keff.org", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("btc.smsys.me", 995, true, 1));
                DNSSeedList.Add(new DNSSeed("electrum.coineuskal.com", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("technetium.network", 50002, true, 1));

                //Litecoin Electrum DNS Seeds
                DNSSeedList.Add(new DNSSeed("backup.electrum-ltc.org", 443, true, 2));
                DNSSeedList.Add(new DNSSeed("electrum-ltc.bysh.me", 50002, true, 2));
                DNSSeedList.Add(new DNSSeed("node.ispol.sk", 50004, true, 2));
                DNSSeedList.Add(new DNSSeed("ltc.rentonisk.com", 50002, true, 2));
                DNSSeedList.Add(new DNSSeed("electrum.ltc.xurious.com", 50002, true, 2));

                //Groestlcoin
                DNSSeedList.Add(new DNSSeed("electrum35.groestlcoin.org", 50002, true, 3));
                DNSSeedList.Add(new DNSSeed("electrum23.groestlcoin.org", 50002, true, 3));
                DNSSeedList.Add(new DNSSeed("electrum10.groestlcoin.org", 50002, true, 3));
                DNSSeedList.Add(new DNSSeed("35.178.21.146", 50002, true, 3));
                DNSSeedList.Add(new DNSSeed("electrum18.groestlcoin.org", 50002, true, 3));
                DNSSeedList.Add(new DNSSeed("18.194.84.135", 50002, true, 3));

                //Bitcoin Cash
                DNSSeedList.Add(new DNSSeed("bitcoin.dragon.zone", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("electrum.imaginary.cash", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("cash.theblains.org", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("electron.coinucopia.io", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("electrumx-cash.1209k.com", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("blackie.c3-soft.com", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("bch.soul-dev.com", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("bch.loping.net", 50002, true, 4));

                //Monacoin
                DNSSeedList.Add(new DNSSeed("electrumx1.monacoin.ninja", 50002, true, 5));
                DNSSeedList.Add(new DNSSeed("electrumx2.tamami-foundation.org", 50002, true, 5));
                DNSSeedList.Add(new DNSSeed("electrumx2.monacoin.nl", 50002, true, 5));
                DNSSeedList.Add(new DNSSeed("electrumx2.monacoin.ninja", 50002, true, 5));

                //Add the Ethereum API Nodes
                EthereumApiNodeList.Add(new EthereumApiNode("https://api.etherscan.io/api", 0, false));
                EthereumApiNodeList.Add(new EthereumApiNode("https://api.myetherwallet.com/eth", 1, false));
                EthereumApiNodeList.Add(new EthereumApiNode("https://cloudflare-eth.com", 2, false));
                //Does not use BlockCypher anymore due to rate limits and API key requirements
                ETH_ATOMICSWAP_ADDRESS = "0xcFd9C086635cee0357729da68810A747B6bC674A"; //The address to the Atomic Swap Contract on Ethereum blockchain
				ERC20_ATOMICSWAP_ADDRESS = "0x1784e5AeC9AD99445663DBCA9462a618BfE545Ac";

            }
            else
            {
                //It uses HTTPS protocol
                NEBLAPI_server = "https://ntp1node.nebl.io/testnet";

                //Various Ports
                //Bitcoin electrum testnet servers
                DNSSeedList.Add(new DNSSeed("testnet1.bauerj.eu", 50002, true, 1));
                DNSSeedList.Add(new DNSSeed("tn.not.fyi", 55002, true, 1));
                DNSSeedList.Add(new DNSSeed("testnet.hsmiths.com", 53012, true, 1));
                DNSSeedList.Add(new DNSSeed("electrumx-test.1209k.com", 50002, true, 1));

                //Litecoin, electrum testnet
                DNSSeedList.Add(new DNSSeed("electrum.ltc.xurious.com", 51002, true, 2));
                DNSSeedList.Add(new DNSSeed("electrum-ltc.bysh.me", 51002, true, 2));

                //Groestlcoin
                DNSSeedList.Add(new DNSSeed("electrum-test2.groestlcoin.org", 51002, true, 3));
                DNSSeedList.Add(new DNSSeed("electrum-test1.groestlcoin.org", 51002, true, 3));

                //Bitcoin Cash
                DNSSeedList.Add(new DNSSeed("blackie.c3-soft.com", 60002, true, 4));
                DNSSeedList.Add(new DNSSeed("testnet.imaginary.cash", 50002, true, 4));
                DNSSeedList.Add(new DNSSeed("electrumx-test-cash.1209k.com", 50002, true, 4));

                //Monacoin
                DNSSeedList.Add(new DNSSeed("electrumx1.testnet.monacoin.ninja", 51002, true, 5));
                DNSSeedList.Add(new DNSSeed("electrumx1.testnet.monacoin.nl", 51002, true, 5));

                //Add the Ethereum testnet API Nodes
                EthereumApiNodeList.Add(new EthereumApiNode("https://api-rinkeby.etherscan.io/api", 0, true)); //Using Rinkeby testnet
                ETH_ATOMICSWAP_ADDRESS = "0xcfd9c086635cee0357729da68810a747b6bc674a"; 
				ERC20_ATOMICSWAP_ADDRESS = "0x80ad941af33b8bB436080652ceaf04213610083D"; //The address to the ERC20 atomic swap contract
				//The address to the Atomic Swap Contract on Ethereum Rinkeby blockchain
               //MyEtherAPI uses Ropsten testnet, not Rinkeby so don't use for testnet
               //Cloudflare doesn't have testnet connection

            }

        }

		public static void FindElectrumServers()
        {
            AddDNSServers();

            //This prevents server changes from breaking the protocol
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            if (File.Exists(App_Path + "/data/electrum_peers.dat") == false)
            {
                //This function finds electrum servers, gets the list of running servers and saves it

                List<string> electrum_list = new List<string>(); //List of all server names
                List<int> electrum_port = new List<int>();

                int total_electrum_types = 0;
                for (int m = 1; m < total_cointypes; m++)
                {
                    //Start at BTC CoinType
                    //Do this for all electrum servers, BTC and LTC
                    int target_port = 0;
                    List<DNSSeed> MyList = new List<DNSSeed>(); //Custom list
                    if (m == 1)
                    {
                        NebliDexNetLog("Finding Bitcoin electrum servers");
                        total_electrum_types++;
                    }
                    else if (m == 2)
                    {
                        NebliDexNetLog("Finding Litecoin electrum servers");
                        total_electrum_types++;
                    }
                    else if (m == 3)
                    {
                        NebliDexNetLog("Finding Groestlcoin electrum servers");
                        total_electrum_types++;
                    }
                    else if (m == 4)
                    {
                        NebliDexNetLog("Finding Bitcoin Cash electrum servers");
                        total_electrum_types++;
                    }
                    else if (m == 5)
                    {
                        NebliDexNetLog("Finding Monacoin electrum servers");
                        total_electrum_types++;
                    }
                    else if (m == 6)
                    {
                        continue; //Ethereum doesn't use DexConnection
                    }
                    for (int i2 = 0; i2 < DNSSeedList.Count; i2++)
                    {
                        if (DNSSeedList[i2].cointype == m)
                        {
                            MyList.Add(DNSSeedList[i2]); //Get the specific coin type only
                        }
                    }

                    //Connect to the DNS servers and download the lists of peers including port
                    int pos = (int)Math.Round(GetRandomNumber(1, MyList.Count)) - 1;
                    //Must convert dns address to ip before using tcp client
                    int nodes_amount = 0;
                    int startrow = electrum_list.Count;
                    for (int i = 0; i < MyList.Count; i++)
                    {
                        try
                        {
                            uint electrum_id = 0;
                            IPAddress address = Dns.GetHostAddresses(MyList[pos].domain)[0].MapToIPv4();
                            target_port = MyList[pos].port;
                            TcpClient client = new TcpClient();
                            //This will wait 15 seconds before moving on (bad connection)
                            if (ConnectSync(client, address, target_port, 15))
                            {
                                NebliDexNetLog("Connected To: " + MyList[pos].domain);
                                client.ReceiveTimeout = 15000; //15 seconds
                                client.SendTimeout = 15000;
                                NetworkStream nStream = client.GetStream(); //Get the stream

                                SslStream secure_nStream = null;
                                if (MyList[pos].ssl == true)
                                {
                                    //Get a secure connection
                                    secure_nStream = new SslStream(nStream, true, new RemoteCertificateValidationCallback(CheckSSLCertificate), null);
                                    secure_nStream.ReadTimeout = 15000;
                                    secure_nStream.WriteTimeout = 15000;
                                    try
                                    {
                                        secure_nStream.AuthenticateAsClient(MyList[pos].domain);
                                    }
                                    catch (Exception e)
                                    {
                                        secure_nStream.Close();
                                        nStream.Close();
                                        client.Close();
                                        throw new Exception("Invalid certificate from server, error: " + e.ToString());
                                    }
                                }
                                else
                                {
                                    nStream.Close();
                                    client.Close();
                                    throw new Exception("This electrum connection must be SSL");
                                }
                                NebliDexNetLog("Securely Connected To: " + MyList[pos].domain);

                                //Create the JSON
                                JObject js = new JObject();
                                js["id"] = electrum_id;
                                js["method"] = "server.version";
                                js["params"] = new JArray("NebliDex", "1.4"); //ElectrumX is now the official repository

                                string json_encoded = JsonConvert.SerializeObject(js);

                                Byte[] data = System.Text.Encoding.ASCII.GetBytes(json_encoded + "\r\n");
                                secure_nStream.Write(data, 0, data.Length); //Write to Server
                                                                            //And then receive the response
                                Byte[] databuf = new Byte[1024]; //Our buffer
                                int packet_size = 1;
                                string response = "";
                                while (packet_size > 0)
                                {
                                    packet_size = NSReadLine(secure_nStream, databuf, 0, databuf.Length); //Read into buffer
                                                                                                          //Get the string
                                    response = response + System.Text.Encoding.ASCII.GetString(databuf, 0, packet_size);
                                    if (packet_size < databuf.Length)
                                    {
                                        //Message is finished
                                        break;
                                    }
                                }
                                NebliDexNetLog(response);

                                js = JObject.Parse(response);
                                if (js["result"] == null)
                                { //Something wrong with this node
                                    secure_nStream.Close();
                                    nStream.Close();
                                    client.Close();
                                    //Try next
                                    throw new Exception("Failed to read data from the secured stream");
                                }

                                //Now find the peers on the network
                                electrum_id++;
                                js = new JObject();
                                js["id"] = electrum_id;
                                js["method"] = "server.peers.subscribe";
                                js["params"] = new JArray();
                                json_encoded = JsonConvert.SerializeObject(js);

                                data = System.Text.Encoding.ASCII.GetBytes(json_encoded + "\r\n");
                                secure_nStream.Write(data, 0, data.Length); //Write to Server

                                //Now receive this response
                                packet_size = 1;
                                response = "";
                                while (packet_size > 0)
                                {
                                    packet_size = NSReadLine(secure_nStream, databuf, 0, databuf.Length); //Read into buffer
                                                                                                          //Get the string
                                    response = response + System.Text.Encoding.ASCII.GetString(databuf, 0, packet_size);
                                    if (packet_size < databuf.Length)
                                    {
                                        //Message is finished
                                        break;
                                    }
                                }

                                NebliDexNetLog("Peers: " + response);

                                //Now take this json and deserialize it
                                js = JObject.Parse(response);

                                //Go through each row of results
                                int current_node_amount = nodes_amount;
                                foreach (JToken row in js["result"])
                                {
                                    //And then the ports
                                    string ip_value = row[0].ToString();
                                    ip_value = ip_value.ToLower().Trim();
                                    if (ip_value.IndexOf("onion",StringComparison.InvariantCulture) > -1) { continue; } //No Tor addresses
									if (ip_value.IndexOf(":",StringComparison.InvariantCulture) > -1) { continue; } //No IPv6 addresses
                                    foreach (JToken port in row[2])
                                    {
                                        string port_value = port.ToString();
                                        if (port_value.StartsWith("s"))
                                        { //Now using SSL
                                            nodes_amount++;
                                            electrum_list.Add(ip_value);
                                            electrum_port.Add(Convert.ToInt32(port_value.Substring(1)));
                                            //Add this information to the list
                                        }
                                    }
                                }

                                //Now add the node I connected to, to this list
                                nodes_amount++;
                                electrum_list.Add(address.ToString());
                                electrum_port.Add(target_port);

                                if (current_node_amount == nodes_amount - 1)
                                {
                                    if (i < MyList.Count - 1)
                                    {
                                        //Should be more, if not, try another server
                                        //Do not finalize the list until client went through all DNS servers
                                        secure_nStream.Close();
                                        nStream.Close();
                                        client.Close();
                                        //Try next
                                        throw new Exception("Server lacked new nodes for connection, only adding server node");
                                    }
                                    else
                                    {
                                        NebliDexNetLog("Server lacked new nodes, but last server on DNS list");
                                    }
                                }

                                //Now add the amount of nodes to the beginning
                                electrum_list.Insert(startrow, "" + nodes_amount); //The number of rows at the beginning of these nodes

                                secure_nStream.Close();
                                nStream.Close();
                                client.Close();

                                break; //We got our list of nodes, leave this loop, and go to next electrum server
                            }
                            else
                            {
                                client.Close();
                                NebliDexNetLog("Connection Timeout: " + MyList[pos].domain);
                            }
                        }
                        catch (Exception e)
                        {
                            //Something went wrong with lookup, skip

                            NebliDexNetLog("Unable to connect to: " + MyList[pos].domain + ", error: " + e.ToString());
                        }
                        pos++;
                        if (pos > MyList.Count - 1) { pos = 0; }
                    }

                    if (nodes_amount == 0)
                    {
                        NebliDexNetLog("Failed to find electrum nodes for blockchain type: " + m);
                        return; //Could not find electrum nodes for all coins
                    }
                }

                //Now create a file that has all these nodes
                try
                {
                    using (System.IO.StreamWriter file =
                        new System.IO.StreamWriter(@App_Path + "/data/electrum_peers.dat", false))
                    {
                        int index = 0;
                        int index2 = 0;
                        for (int i = 0; i < total_electrum_types; i++)
                        {
                            int num = Convert.ToInt32(electrum_list[index]); //Returns amount of nodes
                            file.WriteLine("" + num);
                            index++;
                            for (int i2 = 0; i2 < num; i2++)
                            {
                                file.WriteLine("" + electrum_list[index]);
                                file.WriteLine("" + electrum_port[index2]);
                                index++; index2++;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to write electrum_peers.dat, error: " + e.ToString());
                }
            }

        }

		public static bool CheckSSLCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors != SslPolicyErrors.RemoteCertificateNotAvailable)
            {
                return true;
            }

            NebliDexNetLog("SSL Certificate error: " + sslPolicyErrors);

            // Do not allow this client to communicate with unauthenticated servers.
            return false;
        }

        public static void FindCNServers(bool dialog)
        {
            //This function will find CN servers to connect to, first by using its seed
            if (File.Exists(App_Path + "/data/cn_list.dat") == false)
            {
                //First use the seed to get a list of all the CNs
                List<string> prelim_cn = new List<string>(); //This is a list of IP addresses
                if (DNS_SEED_TYPE == 0)
                {
                    //HTTP seed
                    bool timeout;
                    string resp = HttpRequest(DNS_SEED, "", out timeout);
                    if (resp.Length == 0)
                    {
                        //Unable to connect to seed
                        if (dialog == true)
                        {
							Application.Invoke(delegate
                            {
                                MessageBox(null, "Notice", "Unable to connect to DNS Seed", "OK");
                            });
                        }
                        return;
                    }
                    try
                    {
                        JObject js = JObject.Parse(resp);
                        foreach (JToken row in js["cn_seed"])
                        {
                            string ip_value = row.ToString();
                            bool blacklisted = CheckCNBlacklist(ip_value);
                            if (blacklisted == false)
                            { //Only add this ip, if not on blacklist
                                prelim_cn.Add(ip_value);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        NebliDexNetLog("Unable to get Critical Node List, error: " + e.ToString());
                        return;
                    }

                }
                else
                {
                    //Connect directly to a CN to get a list from it
                    try
                    {
                        IPAddress address = IPAddress.Parse(DNS_SEED);
                        int port = critical_node_port;
                        TcpClient client = new TcpClient();
                        //This will wait 5 seconds before moving on (bad connection)
                        NebliDexNetLog("Trying to connect to DNS Seed: " + DNS_SEED);
                        if (ConnectSync(client, address, port, 5))
                        {
                            //Connect to the critical node and get a list of possible critical nodes
                            NebliDexNetLog("Connected to CN Seed: " + DNS_SEED);
                            client.ReceiveTimeout = 5000; //5 seconds
                            NetworkStream nStream = client.GetStream(); //Get the stream

                            int numpts = 0;
                            int page = 0;
                            do
                            {
                                JObject js = new JObject();
                                js["cn.method"] = "cn.getlist";
                                js["cn.response"] = 0; //This is telling the CN that this is a request
                                js["cn.page"] = page;
                                string json_encoded = JsonConvert.SerializeObject(js);
                                string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                                //Convert it to a JSON
                                js = JObject.Parse(response);
                                string r_method = js["cn.method"].ToString();
                                numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                                if (r_method.Equals("cn.getlist"))
                                {
                                    //Get the list
                                    foreach (JToken row in js["cn.result"])
                                    {
                                        //And get each IP
                                        string ip_value = row["cn.ip"].ToString();
                                        bool blacklisted = CheckCNBlacklist(ip_value);
                                        if (blacklisted == false)
                                        { //Only add this ip, if not on blacklist
                                            prelim_cn.Add(ip_value);
                                        }
                                    }
                                }
                                page++;
                            } while (numpts > 0);

                            nStream.Close();
                            client.Close();
                        }
                        else
                        {
                            client.Close();
                            throw new Exception("Server timeout to DNS Seed");
                        }
                    }
                    catch (Exception e)
                    {
                        if (CN_Nodes_By_IP.Count == 0 && dialog == true)
                        {
							Application.Invoke(delegate
                            {
                                MessageBox(null, "Notice", "No Critical Nodes Found!", "OK");
                            });
                        }
                        NebliDexNetLog("Critical Node parse error: " + DNS_SEED + " error: " + e.ToString());
                        return;
                    }
                }

                //Now find consensus among the CNs regarding our list
                //If a CN IP appears less often than the median, remove it
                List<string> cn_ip = new List<string>();
                List<int> cn_ip_prevalance = new List<int>();
                int cn_server_queried = 0;
                for (int i = 0; i < 10; i++)
                {
                    //Do this for at most 10 CNs
                    //Get random CN from prem_lim list
                    if (prelim_cn.Count <= 0) { break; }
                    int pos = (int)Math.Round(GetRandomNumber(1, prelim_cn.Count)) - 1;
                    try
                    {
                        IPAddress address = IPAddress.Parse(prelim_cn[pos]);
                        int port = critical_node_port;
                        TcpClient client = new TcpClient();
                        //This will wait 5 seconds before moving on (bad connection)
                        if (ConnectSync(client, address, port, 5))
                        {
                            NebliDexNetLog("Client: Connected to: " + prelim_cn[pos]);
                            //Connect to the critical node and get a list of possible critical nodes
                            client.ReceiveTimeout = 5000; //5 seconds
                            NetworkStream nStream = client.GetStream(); //Get the stream

                            int numpts = 0;
                            int page = 0;
                            cn_server_queried++;
                            do
                            {
                                JObject js = new JObject();
                                js["cn.method"] = "cn.getlist";
                                js["cn.response"] = 0; //This is telling the CN that this is a request
                                js["cn.page"] = page;
                                string json_encoded = JsonConvert.SerializeObject(js);
                                string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response     

                                NebliDexNetLog("Get CN List Response: " + response);
                                //Convert it to a JSON
                                js = JObject.Parse(response);
                                string r_method = js["cn.method"].ToString();
                                numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                                if (r_method.Equals("cn.getlist"))
                                {
                                    //Get the list
                                    foreach (JToken row in js["cn.result"])
                                    {
                                        //And get each IP
                                        string ip_value = row["cn.ip"].ToString();

                                        //Check if on blacklist
                                        bool blacklisted = CheckCNBlacklist(ip_value);
                                        if (blacklisted == true)
                                        { //Skip this CN if on blacklist
                                            continue;
                                        }

                                        //Go through our list and add the cn if not present
                                        bool present = false;
                                        for (int i2 = 0; i2 < cn_ip.Count; i2++)
                                        {
                                            if (cn_ip[i2].Equals(ip_value) == true)
                                            {
                                                present = true;
                                                cn_ip_prevalance[i2]++; //Increase its prevalence
                                                break;
                                            }
                                        }

                                        if (present == false)
                                        {
                                            //Add the CN
                                            cn_ip.Add(ip_value);
                                            cn_ip_prevalance.Add(1);
                                        }
                                    }
                                }
                                page++;
                            } while (numpts > 0);

                            nStream.Close();
                            client.Close();

                        }
                        client.Close();
                    }
                    catch (Exception e)
                    {
                        NebliDexNetLog("Critical Node parse error: " + prelim_cn[pos] + " error: " + e.ToString());
                    }
                    prelim_cn.RemoveAt(pos); //Remove this prelim and move on
                }

                //Now go through list and remove CNs that are on less than half of the CNs (less than 5 because we searched 10 CNs)
                int median = (int)Math.Floor((double)cn_server_queried / 2.0);
                //Remove the CNs less than the median from our list
                for (int i = cn_ip.Count - 1; i >= 0; i--)
                {
                    if (cn_ip_prevalance[i] < median)
                    {
                        cn_ip.RemoveAt(i);
                        cn_ip_prevalance.RemoveAt(i);
                    }
                }

                if (cn_ip.Count <= 0)
                {
                    NebliDexNetLog("No Critical Nodes Found");
                    if (dialog == true)
                    {
						Application.Invoke(delegate
                        {
                            MessageBox(null, "Notice", "No Critical Nodes Found!", "OK");
                        });
                    }
                    return;
                }

                //Now write the data to a file
                //Format: DNS_SEED_Type, DNS_Seed
                //For each Critical Node: IP
                lock (CN_Nodes_By_IP)
                {
                    try
                    {
                        using (System.IO.StreamWriter file =
                            new System.IO.StreamWriter(@App_Path + "/data/cn_list.dat", false))
                        {
                            file.WriteLine("" + DNS_SEED_TYPE);
                            file.WriteLine("" + DNS_SEED);
                            for (int i = 0; i < cn_ip.Count; i++)
                            {
                                file.WriteLine(cn_ip[i]);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        NebliDexNetLog("Failed to write cn_list.dat");
                    }
                }
            }

        }

        public static bool AccurateCNOnlineCount()
        {
            //This function will check the CNs currently online and compare them to what we have in our file if it is very different
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

            if (dex == null)
            {
                return true; //No CNs detected
            }

            int total_points = 0;
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
                        SendCNServerAction(dex, 56, "" + page);
                        dex.blockhandle.WaitOne(15000); //This will wait 15 seconds for a response                              
                        if (dex.blockdata == "") { break; }
                        blockdata = dex.blockdata;
                    }

                    JObject js = JObject.Parse(blockdata);
                    numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                    total_points += numpts;
                    page++;
                } while (numpts > 0);
            }
            catch (Exception)
            {
                NebliDexNetLog("Unable to get full list of CNs for TN");
            }

            if (total_points == 0)
            {
                NebliDexNetLog("This CN doesn't have any Critical Nodes. Unusual.");
                //Should not be possible, remove the IP as well
                dex.open = false;
                lock (CN_Nodes_By_IP)
                {
                    CN_Nodes_By_IP.Remove(dex.ip_address[0]); //Remove this CN from our real list
                }
                if (CN_Nodes_By_IP.Count > 0)
                {
                    RecreateCNList(); //Recreate our list without this CN
                }
                else
                {
                    lock (CN_Nodes_By_IP)
                    {
                        File.Delete(@App_Path + "/data/cn_list.dat");
                    }
                }
                ConnectCNServer(false); //Connect to another CN server
                return true;
            }

            if (Math.Abs(CN_Nodes_By_IP.Count - total_points) > 3)
            {
                NebliDexNetLog("Updating CN List, old data found");
                //Our number of CNs doesn't match, tell program to update list
                lock (CN_Nodes_By_IP)
                {
                    File.Delete(@App_Path + "/data/cn_list.dat");
                }
                return false;
            }
            return true;
        }

        public static void ConnectBlockHelperNode()
        {
            if (using_blockhelper == true) { return; }
            //The program will automatically try to connect to the blockhelper node at the blockhelper port
            //The server should be running on the localhost
            try
            {
                TcpClient client = new TcpClient();
                //This will wait 5 seconds before moving on (bad connection)
                NebliDexNetLog("Trying to connect to NebliDex BlockHelper");
                string blockhelper_ip = "127.0.0.1";
                IPAddress address = IPAddress.Parse(blockhelper_ip); //Localhost
                if (ConnectSync(client, address, blockhelper_port, 5))
                {

                    NetworkStream nStream = client.GetStream();
                    nStream.ReadTimeout = 30000; //Wait 30 seconds before timing out on reads
                    nStream.WriteTimeout = 5000;

                    //Get Server version from the node and make sure it matches the client
                    JObject js = new JObject();
                    js["rpc.method"] = "rpc.serverversion";
                    js["rpc.response"] = 0;
                    string json_encoded = JsonConvert.SerializeObject(js);

                    string response = CNWaitResponse(nStream, client, json_encoded);

                    js = JObject.Parse(response);
                    if (js["rpc.result"] == null)
                    {
                        //There is an error
                        nStream.Close();
                        client.Close();
                        throw new System.InvalidOperationException("BlockHelper server error");
                    }
                    else
                    {
                        string version = js["rpc.result"].ToString();
                        int dotpos = version.IndexOf(".");
                        version = version.Substring(0, dotpos); //Get the first number, the protocol number
                        if (version != expected_blockhelper_version_prefix)
                        {
                            //If protocol doesn't match, we can't connect
                            nStream.Close();
                            client.Close();
                            throw new System.InvalidOperationException("BlockHelper server version");
                        }
                    }

                    nStream.WriteTimeout = 15000;
                    DexConnection dex = new DexConnection();
                    dex.ip_address[0] = blockhelper_ip;
                    dex.ip_address[1] = blockhelper_port.ToString();
                    dex.outgoing = true;
                    dex.open = true; //The connection is open
                    dex.contype = 0; //RPC connections are type 0
                    dex.client = client;
                    dex.stream = nStream;
                    lock (DexConnectionList)
                    {
                        if (using_blockhelper == true)
                        {
                            //Race condition detected, this method called from another method at same time
                            nStream.Close();
                            client.Close();
                            throw new System.InvalidOperationException("Race Detected: Already connected to BlockHelper");
                        }
                        using_blockhelper = true;

                        DexConnectionList.Add(dex);
                        //Then go through the list and close any type 5 connections
                        for (int i = 0; i < DexConnectionList.Count; i++)
                        {
                            if (DexConnectionList[i].contype == 5)
                            {
                                DexConnectionList[i].open = false;
                            }
                        }
                    }

                    //BlockHelper doesn't use the callback, instead, messages must be written and received immediately (like HTTP)
                    //The system will lock the connection for the duration of the writeread
                    //Because there is only 1 blockhelper connection for potentially many dex connections, a bottleneck may occur
                    //Consider creating a linked blockhelper connection per incoming dex connection that uses blockhelper
                    //Reset the NTP1 counter
                    ntp1downcounter = 0;
                    NebliDexNetLog("Connected To NebliDex BlockHelper");
                }
                else
                {
                    client.Close();
                    throw new System.InvalidOperationException("Server timeout");
                }
            }
            catch (Exception e)
            {
                //Something went wrong with connecting to the blockhelper local node
                NebliDexNetLog("Failed to connect to the local BlockHelper node :" + e.ToString());
            }
        }

        public static void ConnectCNBlockHelper()
        {
            //The client is trying to connect to a CN that is running a blockhelper
            if (using_cnblockhelper == true) { return; }
            if (critical_node == true) { return; }
            Dictionary<string, CriticalNode> CN_Node_List_Clone = new Dictionary<string, CriticalNode>(CN_Nodes_By_IP);
            if (CN_Node_List_Clone.Count == 0) { return; }

            //Randomly pick a CN from the list that we will attempt to connect
            DexConnection dex = null;
            while (CN_Node_List_Clone.Count > 0)
            {
                int pos = (int)Math.Round(GetRandomNumber(1, CN_Node_List_Clone.Count)) - 1;
                int index = 0;

                foreach (string ip in CN_Node_List_Clone.Keys)
                {
                    if (index == pos)
                    {
                        try
                        {
                            TcpClient client = new TcpClient();
                            NebliDexNetLog("Trying to connect to CN BlockHelper");
                            IPAddress address = IPAddress.Parse(ip); //Localhost
                            if (ConnectSync(client, address, critical_node_port, 10))
                            {
                                NetworkStream nStream = client.GetStream();
                                nStream.ReadTimeout = 10000; //Wait 10 seconds before timing out on reads
                                nStream.WriteTimeout = 5000;

                                //Get Server version from the node and make sure it matches the client
                                JObject js = new JObject();
                                js["cn.method"] = "cn.getblockhelper_status";
                                js["cn.response"] = 0;
                                string json_encoded = JsonConvert.SerializeObject(js);

                                string response = CNWaitResponse(nStream, client, json_encoded);
                                js = JObject.Parse(response);
                                if (js["cn.result"].ToString() == "Active")
                                {
									//This CN has its blockhelper active, use this connection
									nStream.ReadTimeout = System.Threading.Timeout.Infinite;
                                    nStream.WriteTimeout = 15000;
                                    dex = new DexConnection();
                                    dex.ip_address[0] = ip;
                                    dex.ip_address[1] = critical_node_port.ToString();
                                    dex.outgoing = true;
                                    dex.open = true; //The connection is open
                                    dex.contype = 5; //RPC connections are type 0
                                    dex.client = client;
                                    dex.stream = nStream;
                                    lock (DexConnectionList)
                                    {
                                        if (using_cnblockhelper == true)
                                        {
                                            //To address potential concurrency issues
                                            nStream.Close();
                                            client.Close();
                                            CN_Node_List_Clone.Clear();
                                            throw new Exception("Already connected to a CN blockhelper");
                                        }
                                        DexConnectionList.Add(dex);
                                    }
                                    nStream.BeginRead(dex.buf, 0, 1, DexConnCallback, dex);
                                    using_cnblockhelper = true;
                                    ntp1downcounter = 0; //Reset the API counter
                                    NebliDexNetLog("Connected to CN Blockhelper");
                                    break;
                                }
                                else
                                {
                                    nStream.Close();
                                    client.Close();
                                }
                            }
                            else
                            {
                                throw new Exception("Failed to connect to Critical Node at IP: " + ip);
                            }
                        }
                        catch (Exception e)
                        {
                            NebliDexNetLog("Failed to connect to the CN BlockHelper node :" + e.ToString());
                        }
                        CN_Node_List_Clone.Remove(ip);
                        break;
                    }
                    index++;
                }
                if (dex != null) { break; }
            }
        }

		public static void ConnectElectrumServers(int type)
        {
            if (File.Exists(App_Path + "/data/electrum_peers.dat") == false)
            {
                FindElectrumServers(); //Re-download the list
                return;
            }
            //This function will make a persistent connection to many electrum servers
            //If the file is created already, just load electrum info
            int min;
            int max;
            if (type == -1)
            {
                //Connect to all the servers
                min = 0;
                max = total_cointypes - 1;
                //type 0 doesn't use electrum
            }
            else
            {
                min = type - 1;
                max = type;
            }

            //File index
            int f_index = min - 1;

            for (int i = min; i < max; i++)
            {
                //Connect to electrum servers
                if (i == 5)
                {
                    //Ethereum doesn't use electrum servers
                    continue;
                }
                f_index++;
                if (f_index == 0)
                {
                    NebliDexNetLog("Selecting Bitcoin Electrum Server");
                }
                else if (f_index == 1)
                {
                    NebliDexNetLog("Selecting Litecoin Electrum Server");
                }
                else if (f_index == 2)
                {
                    NebliDexNetLog("Selecting Groestlcoin Electrum Server");
                }
                else if (f_index == 3)
                {
                    NebliDexNetLog("Selecting Bitcoin Cash Electrum Server");
                }
                else if (f_index == 4)
                {
                    NebliDexNetLog("Selecting Monacoin Electrum Server");
                }
                string[] electrum_info = new string[2];
                bool ok = SelectRandomElectrum(f_index, electrum_info);
                if (ok == false)
                {
                    NebliDexNetLog("Unable to select electrum server for this blockchain");
					break;
                }
                try
                {
                    IPAddress address = IPAddress.Parse(electrum_info[0]);
                    int port = Convert.ToInt32(electrum_info[1]);
                    TcpClient client = new TcpClient();
                    //This will wait 5 seconds before moving on (bad connection)
                    NebliDexNetLog("Trying to connect to: " + electrum_info[0]);
                    if (ConnectSync(client, address, port, 5))
                    {
                        NebliDexNetLog("Connected To Electrum: " + electrum_info[0]);
                        NetworkStream nStream = client.GetStream();
                        //Get a secure connection
                        SslStream secure_nStream = new SslStream(nStream, true, new RemoteCertificateValidationCallback(CheckSSLCertificate), null);
                        secure_nStream.ReadTimeout = 5000;
                        secure_nStream.WriteTimeout = 5000;
                        try
                        {
                            secure_nStream.AuthenticateAsClient(electrum_info[0]);
                        }
                        catch (Exception e)
                        {
                            secure_nStream.Close();
                            nStream.Close();
                            client.Close();
                            throw new Exception("Invalid certificate from server, error: " + e.ToString());
                        }

                        //Transmit server version to make sure this is accepted or not
                        JObject js = new JObject();
                        js["id"] = 0;
                        js["method"] = "server.version";
                        js["params"] = new JArray("NebliDex", "1.4"); //ElectrumX is now the official repository

                        string json_encoded = JsonConvert.SerializeObject(js);

                        Byte[] data = System.Text.Encoding.ASCII.GetBytes(json_encoded + "\r\n");
                        secure_nStream.Write(data, 0, data.Length); //Write to Server
                                                                    //And then receive the response
                        Byte[] databuf = new Byte[1024]; //Our buffer
                        int packet_size = 1;
                        string response = "";
                        while (packet_size > 0)
                        {
                            packet_size = NSReadLine(secure_nStream, databuf, 0, databuf.Length); //Read into buffer
                                                                                                  //Get the string
                            response = response + System.Text.Encoding.ASCII.GetString(databuf, 0, packet_size);
                            if (packet_size < databuf.Length)
                            {
                                //Message is finished
                                break;
                            }
                        }
                        NebliDexNetLog(response);

                        js = JObject.Parse(response);
                        if (js["result"] == null)
                        {
                            //This server version not accepted
                            //Try another
                            secure_nStream.Close();
                            nStream.Close();
                            client.Close();
                            throw new System.InvalidOperationException("Electrum server version too low");
                        }

                        secure_nStream.ReadTimeout = System.Threading.Timeout.Infinite; //Reset the read timeout to its default
                        secure_nStream.WriteTimeout = 15000;
                        DexConnection dex = new DexConnection();
                        dex.ip_address[0] = electrum_info[0];
                        dex.ip_address[1] = electrum_info[1];
                        dex.outgoing = true;
                        dex.open = true; //The connection is open
                        dex.contype = 1; //Type 1 is now used exclusively for all electrum connections
                        dex.blockchain_type = i + 1; //Standardize to blockchain types
                        dex.electrum_id = 1; //We already used the first one
                        dex.client = client;
                        dex.stream = nStream;
                        dex.secure_stream = secure_nStream;
                        lock (DexConnectionList)
                        {
                            DexConnectionList.Add(dex);
                        }

                        //Setup the callback that runs when data is received into connection
                        secure_nStream.BeginRead(dex.buf, 0, 1, DexConnCallback, dex);

                    }
                    else
                    {
                        client.Close();
                        throw new System.InvalidOperationException("Server timeout");
                    }
                }
                catch (Exception e)
                {
                    //Something went wrong with lookup, delete this electrum server and try again
                    NebliDexNetLog("Unable to connect to: " + electrum_info[0] + ", error: " + e.ToString());
                    if (main_window_loaded == true)
                    {
                        //Detect if the internet is down
                        JArray utxo = GetAddressUnspentTX(null, 3, GetWalletAddress(3));
                        if (utxo == null)
                        {
                            NebliDexNetLog("Internet is likely down right now");
                            if (critical_node == true)
                            {
                                cn_network_down = true; //This will force a redownload of all CN data
                            }
                            return;
                        }
                    }

                    bool success = RemoveElectrumServer(f_index, electrum_info[0]);
                    if (success == false)
                    {
                        //The program will redownload the list on the next connect
                        break; //Will try again next loop
                    }
                    i--; //Go back one and try again
                    f_index--;
                }
            }
        }

        public static void ConnectCNServer(bool dialog)
        {
            if (critical_node == false)
            { //CNs do not load from the file. They get information from each other.
              //This function will connect to a CN server and get data from it
                if (File.Exists(App_Path + "/data/cn_list.dat") == false)
                {
                    FindCNServers(false);
                    return;
                }

                //Load all the CNs into the database.
                LoadCNList();
            }

            //Now select random critical node to use
            //Keep looping until we connect to a node
            DexConnection dex = null;

            //Create a list that is a clone of the CN List
            Dictionary<string, CriticalNode> CN_Node_List_Clone = new Dictionary<string, CriticalNode>(CN_Nodes_By_IP);
            if (critical_node == true)
            {
                //Remove our node from the list
                string my_ip = getPublicFacingIP();
                CN_Node_List_Clone.Remove(my_ip); //This will prevent self connection
            }

            while (CN_Node_List_Clone.Count > 0)
            {
                int pos = (int)Math.Round(GetRandomNumber(1, CN_Node_List_Clone.Count)) - 1;
                int index = 0;

                foreach (string ip in CN_Node_List_Clone.Keys)
                {
                    if (index == pos)
                    {
                        //Use this critical node

                        try
                        {
                            IPAddress address = IPAddress.Parse(ip);
                            TcpClient client = new TcpClient();

                            //This will wait 5 seconds before moving on (bad connection)
                            if (ConnectSync(client, address, critical_node_port, 5))
                            {
                                NebliDexNetLog("Client: Connected To: " + ip);
                                NetworkStream nStream = client.GetStream();

                                dex = new DexConnection();
                                dex.ip_address[0] = ip;
                                dex.ip_address[1] = critical_node_port.ToString();
                                dex.outgoing = true;
                                dex.open = true; //The connection is open
                                dex.contype = 3; //CN connection
                                dex.client = client;
                                dex.stream = nStream;
                                lock (DexConnectionList)
                                {
                                    DexConnectionList.Add(dex);
                                }

                                //Setup the callback that runs when data is received into connection
                                nStream.BeginRead(dex.buf, 0, 1, DexConnCallback, dex);

								//Now get some information from the critical node but first send version and total markets
                                SendCNServerAction(dex, 3, "");
                                dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                                if (dex.blockdata == "")
                                {
                                    dex.open = false;
                                    //This will try to connect to another CN
                                    throw new System.InvalidOperationException("Server Communication Error");
                                }

                                if (dex.blockdata.Equals("Version OK") == false)
                                {
                                    if (CN_Node_List_Clone.Count == 1)
                                    {
                                        string dex_mes = dex.blockdata;
										Application.Invoke(delegate
                                        {
                                            bool error_ok = CheckErrorMessage(dex_mes); 
                                            if (dialog == true && error_ok == true)
                                            {
                                                MessageBox(null, "Notice", dex_mes, "OK");
                                            }
                                        });

                                        if (dex.blockdata.Equals("This Critical Node is pending") == true)
                                        {
                                            NebliDexNetLog("The only CN is pending, please try again later");
                                            //This is the last critical node in network, and its still pending, wait for it to come online
                                            dex.open = false; //Close the connection for now
                                            return; //The last CN is pending, leave the function for now
                                        }
                                    }
                                    else
                                    {
                                        NebliDexNetLog(dex.blockdata);
                                        if (dex.blockdata.Equals("This Critical Node is pending") == true)
                                        {
                                            NebliDexNetLog("Skipping over pending CN node");
                                            //This is a pending node, remove it from clone list
                                            dex.open = false; //Close the connection for now
                                            CN_Node_List_Clone.Remove(ip); //Take this out of the picture but don't remove it
                                            break;
                                        }
                                    }
                                    dex.open = false;
                                    //This will try to connect to another CN
                                    throw new System.InvalidOperationException("Server Communication Error");
								}else{
									// Now grab a list of all the active CNs
                                    NebliDexNetLog("Obtaining market data for all CNs");
                                    try {
                                        int page = 0;
                                        int numpts = 0;
                                        do{
                                            string blockdata="";
                                            lock(dex.blockhandle){
                                                dex.blockhandle.Reset();
                                                SendCNServerAction(dex,56,""+page);
                                                dex.blockhandle.WaitOne(15000); //This will wait 15 seconds for a response                              
                                                if(dex.blockdata == ""){break;}
                                                blockdata = dex.blockdata;
                                            }

                                            JObject js = JObject.Parse(blockdata);
                                            // Go through each row to get the total markets
                                            string r_method = js["cn.method"].ToString();                                           
                                            if(r_method.Equals("cn.getlist")){
                                                //Get the list
                                                numpts = Convert.ToInt32(js["cn.numpts"].ToString());
                                                foreach (JToken row in js["cn.result"])
                                                {
                                                    //And get each market for the CNs
                                                    string ip_value = row["cn.ip"].ToString();
                                                    int totalmark = total_markets;
                                                    if(row["cn.totalmarkets"] != null){ // TODO: Transitional condition
                                                        totalmark = Convert.ToInt32(row["cn.totalmarkets"].ToString());
                                                    }
                                                    if(CN_Nodes_By_IP.ContainsKey(ip_value) == true){
                                                        CN_Nodes_By_IP[ip_value].total_markets = totalmark;
                                                    }                                           
                                                }                               
                                            }                                           
                                            page++;                             
                                        }while(numpts > 0);
                                    } catch (Exception) {
                                        NebliDexNetLog("Unable to get full list of CNs for TN");
                                    }
								}

                                if (critical_node == true)
                                {
                                    reconnect_cn = true; //Request to rebroadcast our new cn status to the network
                                }

                            }
                            else
                            {
                                client.Close();
                                throw new System.InvalidOperationException("Server timeout");
                            }
                        }
                        catch (Exception e)
                        {
                            dex = null;
                            //Something went wrong with lookup, delete this cn server and try again
                            NebliDexNetLog("Client: Unable to connect to: " + ip + ", error: " + e.ToString());
                            if (main_window_loaded == true)
                            {
                                //Detect if the internet is down
                                JArray utxo = GetAddressUnspentTX(null, 3, GetWalletAddress(3));
                                if (utxo == null)
                                {
                                    NebliDexNetLog("Internet is likely down right now");
                                    if (dialog == true)
                                    {
										Application.Invoke(delegate
                                        {
                                            MessageBox(null, "Notice", "No internet connection has been detected", "OK");
                                        });
                                    }
                                    if (critical_node == true)
                                    {
                                        cn_network_down = true; //This will force a redownload of all CN data
                                    }
                                    return;
                                }
                            }

                            CN_Node_List_Clone.Remove(ip); //Remove CN from clone list then
                            if (critical_node == false)
                            {
                                //We do not remove nodes by this method if in CN mode
                                //CNs are removed by random querying
                                lock (CN_Nodes_By_IP)
                                {
                                    CN_Nodes_By_IP.Remove(ip); //Remove this CN from our real list
                                }
                                if (CN_Node_List_Clone.Count > 0)
                                {
                                    RecreateCNList(); //Recreate our list without this CN
                                }
                            }
                        }
                        break; //Leave the for each loop
                    }
                    index++;
                }
                if (dex != null) { break; } //We have a connection, break loop
            }

            if (CN_Node_List_Clone.Count == 0)
            {
                //No more nodes, delete the list
                lock (CN_Nodes_By_IP)
                {
                    File.Delete(@App_Path + "/data/cn_list.dat");
                }
                NebliDexNetLog("All critical nodes are down");
                if (dialog == true)
                {
					Application.Invoke(delegate
                    {
                        MessageBox(null, "Notice", "All critical nodes are down", "OK");
                    });
                }
                return;
            }

        }

		public static void DexConnectionCallback(IAsyncResult asyncResult)
        {
            //This function is called when there is data returned from async callback
            DexConnection dexcon = (DexConnection)asyncResult.AsyncState; //The object passed into the callback
            try
            {
                if (dexcon.open == false) { return; } //Stream is already closed
                int bytesread;
                if (dexcon.secure_stream == null)
                {
                    bytesread = dexcon.stream.EndRead(asyncResult); //Get the bytes to read
                }
                else
                {
                    bytesread = dexcon.secure_stream.EndRead(asyncResult); //Secured stream
                }
                if (bytesread == 0)
                {
                    //Nothing to read as connection has been disconnected
                    NebliDexNetLog("The remote server closed the connection: " + dexcon.ip_address[0] + " (Blockchain " + dexcon.blockchain_type + ")");
                    dexcon.open = false;
                    //May consider reopening the connection or doing it from another function           
                    return;
                }
                else
                {
                    //Something to read, so keep reading it
                    if (dexcon.contype == 1)
                    { //Electrum
                      //Electrum connection, just read the entire line
                        int packet_size = bytesread;
                        string msg = System.Text.Encoding.ASCII.GetString(dexcon.buf, 0, packet_size); ;
                        byte[] read_buf = new byte[256];
                        while (packet_size > 0)
                        {
                            packet_size = NSReadLine(dexcon.secure_stream, read_buf, 0, read_buf.Length); //Read into buffer
                                                                                                          //Get the string
                            msg = msg + System.Text.Encoding.ASCII.GetString(read_buf, 0, packet_size);
                            if (packet_size < read_buf.Length)
                            {
                                //Message is finished
                                break;
                            }
                        }

                        //Apparently there can only be one dexcallback at a time
                        Task.Run(() => ProcessDexResponse(dexcon, msg));
                    }
                    else if (dexcon.contype > 2)
                    {
                        //Critical Node connection or Validation Node connection
                        //Get the total bytes of the packet
                        Byte[] data_size_buf = new Byte[4];
                        data_size_buf[0] = dexcon.buf[0]; //The first byte was received so use that
                        dexcon.stream.Read(data_size_buf, 1, 3); //Get the remaining bytes
                        uint data_size = Bytes2Uint(data_size_buf); //Raw data is little endian
                        if (data_size > 50000)
                        {
                            //Messages should be less than 50kb (Charts are biggest data send)
                            //Transactions can also be pretty big (up to 50 kb accepted)
                            dexcon.open = false; return;
                        }
                        string msg = "";
                        byte[] read_buf = new byte[256];
                        while (data_size > 0)
                        {
                            int read_amount = read_buf.Length;
                            if (read_amount > data_size) { read_amount = (int)data_size; }
                            int read_size = dexcon.stream.Read(read_buf, 0, read_amount);
                            //Get the string
                            msg = msg + System.Text.Encoding.ASCII.GetString(read_buf, 0, read_size);
                            if (data_size - read_size <= 0) { break; }
                            data_size -= Convert.ToUInt32(read_size);
                        }
                        dexcon.msg_count++; //Greater than 30 per 5 seconds is not allowed for normal connections
                        Task.Run(() => ProcessDexResponse(dexcon, msg));
                    }
                    dexcon.lasttouchtime = UTCTime(); //There is activity on this connection
                    if (dexcon.secure_stream == null)
                    {
                        dexcon.stream.BeginRead(dexcon.buf, 0, 1, DexConnCallback, dexcon);
                    }
                    else
                    {
                        dexcon.secure_stream.BeginRead(dexcon.buf, 0, 1, DexConnCallback, dexcon);
                    }
                }
            }
            catch (Exception e)
            {
                dexcon.blockdata = "";
                if (dexcon.outgoing == true)
                {
                    //Server doesn't block
                    dexcon.blockhandle.Set(); //Unblock any blocked connections
                }
                NebliDexNetLog("Connection could not be read: " + dexcon.ip_address[0] + " (" + dexcon.contype + ":v" + dexcon.version + "), error: " + e.ToString());
                dexcon.open = false;
            }
        }

        public static void ProcessDexResponse(DexConnection con, string msg)
        {
            //This will take the message and the connection and process it
            if (con.contype == 1)
            {
                //Electrum
                try
                {
                    //Get the JSON response
                    JObject js = JObject.Parse(msg); //Parse this message

                    //Electrum subscriptions do not have IDs, just method and params, read them too
                    if (js["id"] == null)
                    {
                        //Electrum subscription response
                        string meth = js["method"].ToString();
                        if (meth == "blockchain.numblocks.subscribe")
                        {
                            //We don't subscribe to blockheight anymore
                        }
                    }
                    else
                    {
                        //Normal response
                        int resp_id = Convert.ToInt32(js["id"].ToString()); //The electrum ID should match request id
                        lock (DexConnectionReqList)
                        {
                            for (int i = 0; i < DexConnectionReqList.Count; i++)
                            {
                                if (DexConnectionReqList[i].electrum_con == con && DexConnectionReqList[i].electrum_id == resp_id)
                                {
                                    //Our action
                                    if (DexConnectionReqList[i].requesttype == 1)
                                    {
                                        //Get the server version, do nothing
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 2)
                                    {
                                        //Balance check
										//Get the wallet type
                                        int wallet_type = GetWalletType(con.blockchain_type);   
                                        if (js["result"] == null)
                                        {
                                            //This is 0 balance automatically
											UpdateWalletBalance(wallet_type, 0, 0);
                                        }
                                        else
                                        {
                                            string bal = js["result"]["confirmed"].ToString();
                                            Decimal satoshi = Decimal.Parse(bal);
                                            satoshi = Decimal.Divide(satoshi, 100000000); //Convert to Normal Numbers

                                            string ubal = js["result"]["unconfirmed"].ToString();
                                            Decimal usatoshi = Decimal.Parse(ubal);
                                            usatoshi = Decimal.Divide(usatoshi, 100000000); //Convert to Normal Numbers
											UpdateWalletBalance(wallet_type, satoshi, usatoshi);
                                        }
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 3)
                                    {
                                        //List the unspent tx for a certain address
                                        //Just send the entire message out as blockdata, do no processing
                                        con.blockdata = msg;
                                        con.blockhandle.Set(); //Unblock waiting threads
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 4)
                                    {
                                        //List of the transaction raw hex with metadata
                                        con.blockdata = msg;
                                        con.blockhandle.Set(); //Unblock waiting threads                        
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 5)
                                    { //Received transaction response
                                        con.blockdata = msg; //Send raw message back to transaction
                                        con.blockhandle.Set();
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 6)
                                    {
                                        //Received pong from server
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 7)
                                    {
                                        //This is the address history
                                        con.blockdata = msg;
                                        con.blockhandle.Set(); //Unblock waiting threads
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 8)
                                    {
                                        if (js["error"] != null)
                                        {
											//Couldn't find an estimated fee, use default, don't change it
                                        }
                                        else if (js["result"] != null)
                                        {
                                            //Use this fee as our default
                                            JValue fee = (JValue)js["result"]; //Must convert to JValue in order for it to not autoconvert
											decimal electrum_fee = Decimal.Parse(fee.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                                            if (electrum_fee <= 0)
                                            {
												//Don't change the default
                                            }
                                            else
                                            {
												//Set the fee to this amount
                                                blockchain_fee[con.blockchain_type] = electrum_fee;
                                                //Use Bitcoin fee to estimate Bitcoin Cash fee
                                                if (con.blockchain_type == 1)
                                                {
                                                    blockchain_fee[4] = Math.Round(electrum_fee / 80m, 8);
                                                }
                                            }
                                        }
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 9)
                                    {
                                        //Balance check but blocking
                                        if (js["result"] == null)
                                        {
                                            con.blockdata = "0";
                                            con.blockhandle.Set();
                                        }
                                        else
                                        {
                                            string bal = js["result"]["confirmed"].ToString();
                                            Decimal satoshi = Decimal.Parse(bal);
                                            con.blockdata = satoshi.ToString(); //Keep as satoshi
                                            con.blockhandle.Set();
                                        }
                                    }
                                    else if (DexConnectionReqList[i].requesttype == 10)
                                    {
                                        //Get the header information for the current block
                                        if (js["result"] == null)
                                        {
                                            con.blockdata = "-1";
                                            con.blockhandle.Set();
                                        }
                                        else
                                        {
                                            int height = Convert.ToInt32(js["result"]["height"].ToString());
                                            con.blockdata = height.ToString();
                                            con.blockhandle.Set();
                                        }

                                    }
                                    DexConnectionReqList[i].delete = true; //Request remove this request after we commit the action
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Error parsing Electrum message: " + msg + ", error: " + e.ToString());
                }
            }
            else if (con.contype == 3 || con.contype == 4)
            {
                //CN connection
                try
                {

                    JObject js = JObject.Parse(msg); //Parse this message

                    if (con.outgoing == false)
                    {
                        NebliDexNetLog("Server: received CN message from " + con.ip_address[0] + ": " + msg);
                    }
                    else
                    {
                        NebliDexNetLog("Client: received CN message: " + msg);
                    }

                    int response = Convert.ToInt32(js["cn.response"].ToString()); //This shows if we are requesting a response
                    string method = js["cn.method"].ToString();
                    if (method == "cn.reflectip")
                    {
                        //This function will return the IP of the connected user
                        if (response == 0)
                        {
                            SendCNServerAction(con, 1, "");
                        }
                    }
                    else if (method == "cn.getlist")
                    {
                        //Returns the list of connected pairs
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }
                            //Send to server, so response
                            //Verify if the connected client is a critical node by comparing it to list
							//If not send list with only CN Ips and its total markets
                            if (js["cn.authlevel"] == null)
                            {
                                SendCNServerAction(con, 2, js["cn.page"].ToString());
                            }
                            else
                            {
                                if (CheckIfCN(con) == true)
                                {
                                    //Confirmed CN trying to get list
                                    SendCNServerAction(con, 27, js["cn.page"].ToString());
                                }
                            }
                            //Sends a list 
                        }
                        else
                        {
                            //Unblock the blocking call
                            con.blockdata = msg;
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.myversion")
                    {
                        if (response == 0)
                        {
                            //Don't connections while pending CN status
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 4, "This Critical Node is pending");
                                return;
                            }

                            //The client wants to know if his min_version is ok
                            int cversion = Convert.ToInt32(js["cn.result"].ToString()); //Get client version
							int totalmarket = Convert.ToInt32(js["cn.totalmarkets"].ToString()); //Get the total markets
                            if (cversion < protocol_min_version)
                            {
                                //Not good, return information to client
                                SendCNServerAction(con, 4, "Please upgrade your version of NebliDex by visiting NebliDex.xyz (Not .com)"); //Send client version not good
                                con.open = false; //Close the connection
                            }
                            else if (protocol_version < cversion)
                            {
                                //The client min_version is higher than the server version
                                //Reject the connection as well
                                SendCNServerAction(con, 4, "Your version is above my Server version. Cannot connect.");
                                con.open = false; //Close the connection
                            }
							else if(totalmarket > total_markets)
							{
								//Reject the connection if it has more markets than the server supports
                                SendCNServerAction(con, 4, "Your total markets are greater than my Server markets. Cannot connect.");
                                con.open = false; //Close the connection
							}
                            else
                            {
                                //Send back pong
                                SendCNServerAction(con, 4, "Version OK");
                                con.version = cversion; //Now the connection is ok
                            }
                        }
                        else
                        {
                            //This is message received by client
                            //Probably in blocking thread, so unblock
                            con.blockdata = js["cn.result"].ToString();
                            con.blockhandle.Set(); //Unblock waiting threads                            
                        }
                    }
                    else if (method == "cn.getversion")
                    {
                        if (response == 0)
                        {
                            //The CNs will periodically ping nodes asking for their version
                            //And remove them if they are below the min version
                            if (js["cn.authlevel"] != null)
                            {
                                //Check if CN
                                if (CheckIfCN(con) == false)
                                {
                                    //If not a CN, let the possible cn know
                                    SendCNServerAction(con, 54, "Not a CN");
                                    return;
                                }
                            }
                            SendCNServerAction(con, 55, "");
                        }
                    }
                    else if (method == "cn.chart24h")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            //Server received this information, gather chart data and send it as string
                            SendCNServerAction(con, 7, js["cn.result"].ToString()); //Return the 24hr chart
                        }
                        else
                        {
                            con.blockdata = msg;
                            con.blockhandle.Set();
                            //Allow client to handle data               
                        }
                    }
                    else if (method == "cn.chart7d")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            //Server received this information, gather chart data and send it as string
                            SendCNServerAction(con, 9, js["cn.result"].ToString()); //Return the 7day chart
                        }
                        else
                        {
                            con.blockdata = msg;
                            con.blockhandle.Set();
                            //Allow client to handle data       
                        }
                    }
                    else if (method == "cn.openorders")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            if (js["cn.authlevel"] != null)
                            {
                                //Check if critical node first
                                bool good = CheckIfCN(con); //First check if this is actually a CN
                                if (good == false) { SendCNServerAction(con, 21, "Not a CN"); return; }
                                //This is open orders with ip addresses and ports
                                SendCNServerAction(con, 51, js["cn.result"].ToString() + ":" + js["cn.page"].ToString());
                                return;
                            }

                            //Server received this information, gather chart data and send it as string
                            SendCNServerAction(con, 11, js["cn.result"].ToString() + ":" + js["cn.page"].ToString());
                            //Return the open orders for market
                        }
                        else
                        {
                            con.blockdata = msg;
                            con.blockhandle.Set();
                            //Allow client to handle data       
                        }
                    }
                    else if (method == "cn.recenttrades")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            //Server received this information, gather recent trades data and send it as string
                            SendCNServerAction(con, 13, js["cn.marketpage"].ToString());
                            //Return the recent trades
                        }
                        else
                        {
                            con.blockdata = msg;
                            con.blockhandle.Set();
                            //Allow client to handle data
                        }
                    }
                    else if (method == "cn.syncclock")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }

                            //Server received this information, return clock data
                            SendCNServerAction(con, 15, "");
                            //Return the recent trades
                        }
                        else
                        {
                            con.blockdata = msg;
                            con.blockhandle.Set();
                            //Allow client to handle data
                        }
                    }
                    else if (method == "cn.ping")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version && con.contype != 4)
                            { //This function requires minimum version if not validating
                                con.open = false; return;
                            }

                            //Server received this information, return pong
                            SendCNServerAction(con, 5, ""); //Client doesn't care about pong
                        }
                    }
                    else if (method == "cn.sendorder")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }

                            //We received an order, now evaluate it
                            OpenOrder ord = new OpenOrder();
                            string emsg = EvaluateNewOrder(con, js, ord);
                            SendCNServerAction(con, 18, emsg); //Send the response

                            if (emsg == "Order OK")
                            {
                                //Now it's time to broadcast this order to everyone linked
                                BroadcastNewOrder(con, ord); //This is only a CN job
                            }
                        }
                        else
                        {
                            con.blockdata = js["cn.result"].ToString(); //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.neworder")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true)
                            { //Client receives from CN for inclusion
                              //We received this from a CN (more trusted), evaluate the relayed order message
                                if (critical_node == false)
                                {
                                    OpenOrder ord = new OpenOrder();
                                    EvaluateRelayedOrder(con, js, ord, false); //It will be added if passes checks & unique                                 
                                }
                                //Critical Node will get another message sent directly
                            }
                            else
                            {
                                //The message was sent to server
                                //This is a CN that received the message
                                bool good = CheckIfCN(con); //First check if this is actually a CN
                                if (good == false) { SendCNServerAction(con, 21, "Not a CN"); return; } //May consider punishing connection for imitating CN
                                if (js["cn.authlevel"] == null) { SendCNServerAction(con, 21, "Not CN Only Message"); return; } //CN received it, but not meant for CN, disregard

                                //Relay should be last thing checked in case message from non CN
                                bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                                if (received == true) { SendCNServerAction(con, 21, "Order Already Received"); return; } //We already received this message already

                                //New message from CN, now evaluate order contents
                                OpenOrder ord = new OpenOrder();
                                good = EvaluateRelayedOrder(con, js, ord, true); //It will be added if passes checks & unique
                                if (good == true)
                                {
                                    //Now relay this message as well to connected peers
                                    SendCNServerAction(con, 21, "Order Received"); //This should close the temporary connection
                                    RelayNewOrder(con, js); //Take the message and rebroadcast it to peers and other CNs
                                }
                                else
                                {
                                    SendCNServerAction(con, 21, "Invalid Order Data");
                                }
                            }
                        }
                    }
                    else if (method == "cn.cancelorder")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }

                            //We received a cancellation notice from our local maker
                            js["order.ip"] = con.ip_address[0].ToString();
                            js["order.port"] = con.ip_address[1].ToString();
                            js["order.cn_ip"] = getPublicFacingIP();
                            bool allowed = CancelMarketOrder(js, true);

                            if (allowed == true)
                            {
                                js["relay.nonce"] = GenerateHexNonce(24); //Used for relay messages
                                js["relay.timestamp"] = UTCTime();
                                //Add to message relay so we don't receive it again
                                CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));

                                //Now it's time to broadcast this cancel request order to everyone linked
                                RelayCancelOrder(con, js); //This is only a CN job
                            }
                        }
                    }
                    else if (method == "cn.relaycancelorder")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }
                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true)
                            { //Client receives from CN for inclusion
                              //We received this from a CN (more trusted), evaluate the relayed order message
                                if (critical_node == false)
                                {
                                    CancelMarketOrder(js, false);
                                }
                                //Critical Node will get another message sent directly
                            }
                            else
                            {
                                //The message was sent to server
                                //This is a CN that received the message
                                bool good = CheckIfCN(con); //First check if this is actually a CN
                                if (good == false) { SendCNServerAction(con, 21, "Not a CN"); return; } //May consider punishing connection for imitating CN
                                bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                                if (received == true) { SendCNServerAction(con, 21, "Cancel Order Already Received"); return; } //We already received this message already

                                //New cancel request from another CN, evaluate
                                good = CancelMarketOrder(js, true);
                                if (good == true)
                                {
                                    //Now relay this message as well to connected peers
                                    SendCNServerAction(con, 21, "Cancel Order Received"); //This should close the temporary connection
                                    RelayCancelOrder(con, js); //Take the message and rebroadcast it to peers and other CNs
                                }
                                else
                                {
                                    SendCNServerAction(con, 21, "Invalid Cancel Order Data");
                                }
                            }
                        }
                    }
                    else if (method == "cn.newcn")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }
                            //First check if message already relayed
                            bool from_cn = CheckIfCN(con);
                            string error_msg = "";
                            if (from_cn == false || js["relay.nonce"] == null)
                            { //First connection
                                string from_ip = js["cn.ip"].ToString();
                                if (from_ip.Equals(con.ip_address[0]) == false)
                                {
                                    //IP must match sender if not a CN sending it
                                    error_msg = "CN Rejected: Client IP must match sending IP";
                                }
                                else
                                {
                                    error_msg = EvaluateCNRequest(js, false);
                                }
                            }
                            else
                            {
                                error_msg = EvaluateCNRequest(js, true); //No need to check the connection
                                if (error_msg.Length > 0)
                                {
                                    //Send the client, their request was rejected
                                    SendCNServerAction(con, 26, error_msg);
                                    return;
                                }
                            }

                            if (error_msg.Length > 0)
                            {
                                //Send the client, their request was rejected
                                SendCNServerAction(con, 26, error_msg);
                                return;
                            }

                            //Otherwise broadcast/relay CN info
                            SendCNServerAction(con, 26, "CN Accepted");
                            RelayNewCN(con, js);
                        }
                        else
                        {
                            //Client gets a response that CN is rejected or accepted
                            con.blockdata = js["cn.result"].ToString(); //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.newcnstatus")
                    {
                        if (response == 0)
                        {
                            //This should be a new connection trying to figure out if I'm actually a new CN
                            //First make sure this computer is configured correctly to identify CNs
                            if (CheckIfCN(con) == false)
                            {
                                SendCNServerAction(con, 54, "Unrecognized CN IP");
                                return;
                            }

                            string my_ip = getPublicFacingIP();
                            if (CN_Nodes_By_IP.ContainsKey(my_ip) == false)
                            {
                                //We are trying to become a CN
                                SendCNServerAction(con, 54, "New CN Pending");
                                return;
                            }
                            else
                            {
                                if (CN_Nodes_By_IP[my_ip].signature_ip == null || CN_Nodes_By_IP[my_ip].rebroadcast == true)
                                {
                                    //Previous critical node exists, but we are replacing it
                                    SendCNServerAction(con, 54, "New CN Pending");
                                }
                                else
                                {
                                    SendCNServerAction(con, 54, "Critical Node Already Exists.");
                                }
                                return;
                            }
                        }
                    }
                    else if (method == "cn.getcooldownlist")
                    {
                        if (response == 0)
                        {
                            bool from_cn = CheckIfCN(con);
                            if (from_cn == false)
                            {
                                return; //This is not a CN so cannot get any data
                            }

                            //Otherwise return the page requested
                            SendCNServerAction(con, 29, js["cn.page"].ToString());
                        }
                        else
                        {
                            con.blockdata = msg; //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.getorderrequestlist")
                    {
                        if (response == 0)
                        {
                            bool from_cn = CheckIfCN(con);
                            if (from_cn == false)
                            {
                                return; //This is not a CN so cannot get any data
                            }

                            //Otherwise return the page requested
                            SendCNServerAction(con, 31, js["cn.page"].ToString());
                        }
                        else
                        {
                            con.blockdata = msg; //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.getchartprices")
                    {
                        if (response == 0)
                        {
                            bool from_cn = CheckIfCN(con);
                            if (from_cn == false)
                            {
                                //Make sure the version is current then
                                if (con.version < protocol_min_version)
                                { //This function requires minimum version
                                    con.open = false; return;
                                }
                            }

                            //Otherwise return the page requested
                            SendCNServerAction(con, 34, js["cn.timepage"].ToString());
                        }
                        else
                        {
                            con.blockdata = msg; //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.getvolume")
                    {
                        if (response == 0)
                        {
                            //Anyone can grab the volume for the market
                            SendCNServerAction(con, 36, js["cn.market"].ToString());
                        }
                        else
                        {
                            con.blockdata = msg; //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.getlastprice")
                    {
                        if (response == 0)
                        {
                            //Anyone can grab the price for the market
                            SendCNServerAction(con, 49, js["cn.market"].ToString());
                        }
                        else
                        {
                            con.blockdata = msg; //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.sendorderrequest")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }

                            //We received an order request, now evaluate it
                            OrderRequest req = new OrderRequest();
                            string emsg = EvaluateNewOrderRequest(con, js, req);
                            SendCNServerAction(con, 37, emsg); //Send the response

                            if (emsg == "Order Request OK")
                            {
                                //Convert the request to a JSON
                                //Added the relay
                                JObject json = NewOrderRequestJSON(req); //Returns a JSON object
                                                                         //Add to Relay
                                CheckMessageRelay(json["relay.nonce"].ToString(), Convert.ToInt32(json["relay.timestamp"].ToString()));
                                RelayOrderRequest(con, json); //This is only a CN job
                            }
                        }
                        else
                        {
                            con.blockdata = js["cn.result"].ToString(); //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.relayorderrequest")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            //First check if from another CN
                            bool from_cn = CheckIfCN(con);
                            bool good = false;
                            if (from_cn == false)
                            {
                                SendCNServerAction(con, 38, "Not a CN");
                                return;
                            }

                            //Check CN status before checking if message received
                            bool already = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                            if (already == true)
                            {
                                SendCNServerAction(con, 38, "Order Request Already Received");
                                return;
                            }

                            good = EvaluateRelayOrderRequest(js);

                            if (good == false)
                            {
                                //Send the client, their request was rejected
                                SendCNServerAction(con, 38, "Order Request Rejected");
                                //This will free up the blocked CN
                                return;
                            }

                            //Otherwise broadcast/relay CN info
                            SendCNServerAction(con, 38, "Order Request Accepted");
                            RelayOrderRequest(con, js);
                        }
                        else
                        {
                            //Client gets response for order request
                            con.blockdata = js["cn.result"].ToString(); //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.pendorder")
                    {
                        if (response == 0 && con.outgoing == true)
                        {
                            //Pend an order on the charts if its visible
                            ExchangeWindow.PendOrder(js["cn.result"].ToString());
                        }
                    }
                    else if (method == "cn.relayshoworder")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }
                            if (con.outgoing == true)
                            {
                                //Showing the order again from pend
                                ExchangeWindow.ShowOrder(js["cn.result"].ToString());
                            }
                            else
                            {
                                //Relay the showorder message across the network
                                //The message was sent to server
                                //This is a CN that received the message
                                bool good = CheckIfCN(con); //First check if this is actually a CN
                                if (good == false) { SendCNServerAction(con, 44, "Not a CN"); return; } //May consider punishing connection for imitating CN
                                bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                                if (received == true) { SendCNServerAction(con, 44, "Show Order Already Received"); return; } //We already received this message already
                                bool shown = ExchangeWindow.ShowOrder(js["cn.result"].ToString());
                                if (shown == false) { SendCNServerAction(con, 44, "Order Doesn't Exist"); return; }
                                //Now forward the validation info to next nodes
                                SendCNServerAction(con, 44, "Show Order Relay Accepted");
                                RelayShowOrder(con, js);
                            }
                        }
                    }
                    else if (method == "cn.tradeavail")
                    {
                        if (response == 0 && con.outgoing == true)
                        {
                            //The only person who receives this message should be the TN that created the order
                            bool check = VerifyTradeRequest(js); //This will check the amount against our open order
                            JObject tjs = new JObject();
                            tjs["cn.method"] = "cn.tradeavail";
                            tjs["cn.response"] = 1; //Responding to the CN
                            tjs["cn.order_nonce"] = js["cn.order_nonce"];
                            tjs["trade.maker_send_add"] = js["trade.maker_send_add"];
                            tjs["trade.maker_receive_add"] = js["trade.maker_receive_add"];
                            if (check == false)
                            {
                                //Send rejection of trade across network
                                tjs["cn.result"] = "Trade Rejected"; //This will convert the OrderRequest to closed
                                SendCNServerAction(con, 40, JsonConvert.SerializeObject(tjs));
                            }
                            else
                            {
                                tjs["cn.result"] = "Trade Accepted"; //This will convert the OrderRequest to acknowledged
                                SendCNServerAction(con, 40, JsonConvert.SerializeObject(tjs));

                                int who_validate = Convert.ToInt32(js["cn.who_validate"].ToString());
                                if (who_validate == 0)
                                {
                                    //I will validate so find validation node
                                    FindValidationNode(con, js["cn.order_nonce"].ToString(), who_validate);
                                }
                            }
                        }
                        else if (response == 1 && con.outgoing == false)
                        {
                            //The CN receives this from the TN
                            bool check = VerifyTradeConnection(con, js);
                            if (check == false) { return; }
                            //Now broadcast this message to other CNs
                            js["cn.method"] = "cn.relaytradeavail"; //Change the method
                            js["cn.response"] = 0; //Change response
                            js["relay.nonce"] = GenerateHexNonce(24); //Used for relay messages
                            js["relay.timestamp"] = UTCTime();
                            //Add to message relay so we don't receive it again
                            CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                            RelayTradeMessage(con, js);
                        }
                    }
                    else if (method == "cn.relaytradeavail")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }

                            //First check if from another CN
                            bool from_cn = CheckIfCN(con);
                            if (from_cn == false)
                            {
                                SendCNServerAction(con, 41, "Not a CN");
                                return;
                            }

                            bool already = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                            if (already == true)
                            {
                                SendCNServerAction(con, 41, "Trade Message Already Received");
                                return;
                            }

                            //Otherwise broadcast/relay CN info
                            SendCNServerAction(con, 41, "Trade Message Accepted");
                            RelayTradeMessage(con, js);
                        }
                        else
                        {
                            //Client gets response for order request
                            con.blockdata = js["cn.result"].ToString(); //The message
                            con.blockhandle.Set();
                        }
                    }
                    else if (method == "cn.trademessage")
                    {
                        if (response == 0 && con.outgoing == true && main_window_loaded == true)
                        {
                            //The only person who receives this message should be the TN that created the request
                            if (js["cn.who_validate"] == null)
                            {
                                //Rejected Message
                                //Show rejection message
                                if (main_window_loaded == false) { return; }
								bool error_ok = CheckErrorMessage(js["cn.result"].ToString()); //Check list of authorized error messages
                                if (error_ok == false) { return; } //Not standard error message, don't show it
                                main_window.showTradeMessage(js["cn.result"].ToString()); //Pop up messsage box
                            }
                            else
                            {
                                //Accepted message
                                int who_validate = Convert.ToInt32(js["cn.who_validate"].ToString());
                                string order_nonce = js["cn.order_nonce"].ToString();

                                //Mark the taker order request as accepted
                                lock (MyOpenOrderList)
                                {
                                    for (int i = 0; i < MyOpenOrderList.Count; i++)
                                    {
                                        if (MyOpenOrderList[i].is_request == true && MyOpenOrderList[i].order_nonce == order_nonce)
                                        {
                                            //Taker order
                                            if (MyOpenOrderList[i].order_stage > 0)
                                            {
                                                NebliDexNetLog("Already accepted maker order, disregarding this duplicate message");
                                                return;
                                            }
                                            MyOpenOrderList[i].order_stage = 1; //Set message as accept taker offer
                                            break;
                                        }
                                    }
                                }

                                if (who_validate == 1)
                                {
                                    FindValidationNode(con, order_nonce, who_validate);
                                }
                            }
                        }
                    }
                    else if (method == "cn.getvalidator")
                    {
                        if (response == 0)
                        {
                            //CN has received a get validator request from a TN
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }

                            JObject valinfo = GetCNValidator(con, js); //This will find a validating node for the request
                            if (valinfo == null) { return; } //Something weird occurred
                            RelayValidatorInfo(con, valinfo, true); //Relay information from validator, this will go to taker
                        }
                    }
                    else if (method == "cn.orderrequestexist")
                    { //This is a request to a potential validator
                        if (response == 0)
                        {
                            bool from_cn = CheckIfCN(con);
                            if (from_cn == false)
                            {
                                return; //This is not a CN so cannot get any data
                            }

                            int cversion = Convert.ToInt32(js["cn.server_minversion"].ToString());
                            if (cversion > protocol_version)
                            {
                                //Servers need to have the same version otherwise, validation will be rejected
                                SendCNServerAction(con, 43, "My Server Version Is Obsolete. Can't Validate.");
                                return;
                            }

                            string wal_msg = "";
                            bool wallet_avail = CheckWalletBalance(3, cn_ndex_minimum, ref wal_msg);
                            if (wallet_avail == false)
                            {
                                SendCNServerAction(con, 43, "My Server currently not available to validate due to wallet in use.");
                                return;
                            }

                            //This checks to make sure the server is running smoothly and not stuck validating
                            if (UTCTime() - lastvalidate_time > 60 * 30)
                            {
                                //It's been more than 30 minutes since the validating method was used, server may be overwhelmed
                                SendCNServerAction(con, 43, "My Server currently not available to validate due to too many validating trades.");
                                return;
                            }

                            bool exist = false;
                            lock (OrderRequestList)
                            {
                                for (int i = 0; i < OrderRequestList.Count; i++)
                                {
                                    if (OrderRequestList[i].order_nonce_ref.Equals(js["cn.order_nonce"].ToString()) == true && OrderRequestList[i].order_stage < 3)
                                    {
                                        //Must also have maker information that was propagated through network by cn.relaytradeavail
                                        if (OrderRequestList[i].order_stage < 1)
                                        { //The tradeavail hasn't reached this CN yet
                                            SendCNServerAction(con, 43, "My Server doesn't have the Maker Info Yet");
                                            return;
                                        }

                                        if (OrderRequestList[i].market > total_markets - 1)
                                        {
                                            //We don't have this market yet due to our version being old
                                            SendCNServerAction(con, 43, "My Server doesn't have this Market Yet.");
                                            return;
                                        }

                                        //Make myself the validator

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

                                        OrderRequestList[i].validator_pubkey = my_pubkey;
                                        exist = true; break; //Order request exists here
                                    }
                                }
                            }

                            if (exist == false)
                            {
								//No order request found by this CN, shouldn't really happen too often but may happen for unsupported markets
                                SendCNServerAction(con, 43, "Order Request Not Found");
                            }
                            else
                            {
                                //Order request was found
                                SendCNServerAction(con, 43, "Order Request Exists");
                            }
                        }
                    }
                    else if (method == "cn.relayvalidator")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }
                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true)
                            { //Client receives from CN for inclusion
                              //We received this from a CN (more trusted), evaluate the relayed order message
                                if (critical_node == false)
                                {
                                    //Taker received validation information, start validation
                                    bool ok = TakerReceiveValidatorInfo(con, js); //The end of this should produce an encrypted hex string to validation node with fee
                                    if (ok == false)
                                    {
                                        NebliDexNetLog("Taker failed to validate trade, order request will close in a few minutes");
                                    }
                                }
                            }
                            else
                            {
                                //The message was sent to server
                                //This is a CN that received the message
                                bool good = CheckIfCN(con); //First check if this is actually a CN
                                if (good == false) { SendCNServerAction(con, 44, "Not a CN"); return; } //May consider punishing connection for imitating CN
                                bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                                if (received == true) { SendCNServerAction(con, 44, "Validator Info Already Received"); return; } //We already received this message already

                                //Now forward the validation info to next nodes
                                SendCNServerAction(con, 44, "Validator Info Accepted");
                                RelayValidatorInfo(con, js, true); //We are sending the initial information to the taker now
                            }
                        }
                    }
                    else if (method == "cn.validatorgetinfo")
                    {
                        if (response == 0)
                        {
                            //CN Validator has received request for trade information to complete trade
                            JObject valinfo = ValidatorGetInfo(con, js); //This request must come from maker of trade
                            if (valinfo == null) { SendCNServerAction(con, 45, "No Info Available"); return; } //Something weird occurred
                            SendCNServerAction(con, 46, JsonConvert.SerializeObject(valinfo));
                        }
                    }
                    else if (method == "cn.validator_takerinfo")
                    {
                        if (response == 0)
                        {
                            //CN Validator has received request for trade information to complete trade
                            if (js["trade.validator_mytx"] == null) { return; } //Encrypted tx
                            if (con.contype != 4) { return; } //Only validator nodes can do this
                            JObject relayinfo = CNValidateTxFees(js, con, false); //This request must come from maker of trade
                            if (relayinfo == null) { return; } //Something weird occurred

                            //Relay across network that taker has sent to contract address                      
                            RelayValidatorInfo(con, relayinfo, false); //Now send information to the maker  
                        }
                    }
                    else if (method == "cn.validator_notifymaker")
                    {
                        if (response == 0)
                        {
                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true)
                            { //Client receives from CN for inclusion
                              //We received this from a CN (more trusted), evaluate the relayed order message
                                if (critical_node == false)
                                {
                                    //Maker received notification from validation node
                                    bool ok = MakerReceiveValidatorInfo(con, js);
                                    if (ok == false)
                                    {
                                        NebliDexNetLog("Your node rejected this trade request");
                                    }
                                }
                            }
                            else
                            {
                                //The message was sent to server
                                //This is a CN that received the message
                                bool good = CheckIfCN(con); //First check if this is actually a CN
                                if (good == false) { SendCNServerAction(con, 44, "Not a CN"); return; } //May consider punishing connection for imitating CN
                                bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                                if (received == true) { SendCNServerAction(con, 44, "Validator Info Already Received"); return; } //We already received this message already

                                //Now forward the validation info to next nodes
                                SendCNServerAction(con, 44, "Validator Info Accepted");
                                RelayValidatorInfo(con, js, false); //Notify maker messages will need to go to maker
                            }
                        }
                    }
                    else if (method == "cn.validator_makerconfirm")
                    {
                        if (response == 0)
                        {
                            //CN Validator has received maker confirmation of trade information
                            if (js["trade.maker_send_add"] == null) { return; } //Encrypted tx
                            if (con.contype != 4) { return; } //Only validator nodes can do this
                            JObject responseinfo = CNValidateTxFees(js, con, true); //This request must come from maker of trade
                            if (responseinfo == null)
                            {
                                //Tell maker trade will be canceled
                                return;
                            }
                        }
                    }
                    else if (method == "cn.validator_takerconfirm")
                    {
                        if (response == 1)
                        {
                            //This is the taker responding that it has confirmed the contract address
                            //If it doesn't respond, validator will alert maker to abort
                            if (con.outgoing == true || con.contype != 4) { return; } //Should be from tn connected to validator
                            con.blockdata = js["cn.result"].ToString(); //The message
                            con.blockhandle.Set(); //Free the validator to continue processing                          
                        }
                        else if (response == 0)
                        {
                            if (con.outgoing == false || con.contype != 4) { return; } //Should be from CN
                                                                                       //Evaluate the maker information then respond to CN
                            TakerConfirmTrade(con, js);
                        }
                    }
                    else if (method == "cn.relaycancelrequest")
                    {
                        if (response == 0)
                        {
                            if (critical_node_pending == true && critical_node == true)
                            {
                                //Not in a state to respond with an accurate result
                                SendCNServerAction(con, 54, "Critical Node Pending");
                                return;
                            }
                            //The message was sent to server
                            //This is a CN that received the message
                            bool good = CheckIfCN(con); //First check if this is actually a CN
                            if (good == false) { SendCNServerAction(con, 44, "Not a CN"); return; } //May consider punishing connection for imitating CN
                            bool received = CheckMessageRelay(js["relay.nonce"].ToString(), Convert.ToInt32(js["relay.timestamp"].ToString()));
                            if (received == true) { SendCNServerAction(con, 44, "Order Request Cancel Already Received"); return; } //We already received this message already

                            //Now forward the validation info to next nodes
                            SendCNServerAction(con, 44, "Cancel Order Request Info Accepted");
                            RelayCloseOrderRequest(con, js);
                        }
                    }
                    else if (method == "cn.trade_complete")
                    { //Show the completed trade to TNs
                        if (response == 0)
                        {
                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true)
                            { //Client receives from CN for inclusion
                              //We received this from a CN (more trusted), evaluate the relayed order message
                                if (critical_node == false)
                                {
                                    //regular node received this message, critical nodes don't care about this message
                                    int market = Convert.ToInt32(js["trade.market"].ToString());
                                    int type = Convert.ToInt32(js["trade.type"].ToString());
                                    decimal trade_price = Convert.ToDecimal(js["trade.price"].ToString(), CultureInfo.InvariantCulture);
                                    decimal trade_amount = Convert.ToDecimal(js["trade.amount"].ToString(), CultureInfo.InvariantCulture);
                                    string order_nonce = js["trade.order_nonce"].ToString();
                                    int trade_time = UTCTime();
                                    trade_time = Convert.ToInt32(js["trade.complete_time"].ToString());
                                    ExchangeWindow.AddRecentTradeToView(market, type, trade_price, trade_amount, order_nonce, trade_time);
                                }
                            }
                        }
                    }
                    else if (method == "cn.validator_taker_canceltrade")
                    { //Taker receives this when maker rejects the trade / trade timeouts
                        if (response == 0)
                        {
                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true && con.contype == 4 && critical_node == false)
                            { //From validation node
                                string order_nonce = js["cn.order_nonce"].ToString();
                                int reqtime = Convert.ToInt32(js["cn.order_utctime"].ToString());

                                //Validator can also cancel maker transaction if hasn't sent balance yet
                                string type_string = GetMyTransactionData("type", reqtime, order_nonce);
                                if (type_string.Length == 0)
                                {
                                    //Not found
                                    return;
                                }

                                int type = Convert.ToInt32(type_string);
								if (type == 0)
                                {
                                    //Can cancel taker transaction at any point until it pulls the balance from the maker contract
                                    SetMyTransactionData("type", 6, reqtime, order_nonce); //Set pending cancel and wait until taker contract expires to make sure
                                                                                           //Update my trade history to cancelled
                                    string txhash = GetMyTransactionData("txhash", reqtime, order_nonce);
                                    UpdateMyRecentTrade(txhash, 2);
                                    int cointype = Convert.ToInt32(GetMyTransactionData("cointype", reqtime, order_nonce));
                                    //Make the wallet availabe to trade again for taker                                 
                                    UpdateWalletStatus(cointype, 0);
                                    //Also cancel the fee transaction as well (validator will receive fee if trade information exchanged)
                                    CancelFeeWithdrawalMonitor(reqtime, order_nonce);
                                    //Make the fee wallet availabe to trade again for maker                             
                                    UpdateWalletStatus(3, 0);

                                    con.open = false; //Close the validator

                                    //The other half of this will get canceled the normal way (through cancelmarketorder)
                                }
                            }
                        }
                    }
                    else if (method == "cn.validator_maker_canceltrade")
                    { //Maker receives message when taker fails to fund account
                        if (response == 0)
                        {
                            //Both the TN and CNs and receive this message from a critical node
                            if (con.outgoing == true && con.contype == 4 && critical_node == false)
                            { //From validation node
                                string order_nonce = js["cn.order_nonce"].ToString();
                                int reqtime = Convert.ToInt32(js["cn.order_utctime"].ToString());

                                //Validator can also cancel maker transaction if hasn't sent balance yet
                                string type_string = GetMyTransactionData("type", reqtime, order_nonce);
                                if (type_string.Length == 0)
                                {
                                    //Not found
                                    return;
                                }

                                int type = Convert.ToInt32(type_string);
								if(type == 5){
                                    //Waiting for taker, then we can cancel otherwise, we can't
                                    string secret_hash = GetMyTransactionData("atomic_secret_hash",reqtime,order_nonce); //Get the secret_hash before canceling                                 
                                    int cointype = Convert.ToInt32(GetMyTransactionData("cointype",reqtime,order_nonce));
                                    SetMyTransactionData("type",3,reqtime,order_nonce); //Cancel trade
                                    UpdateMyRecentTrade(secret_hash,2);
                                    //Update my trade history to cancelled                                                                      
                                    //Also cancel the fee transaction as well (validator will receive fee if trade information exchanged)
                                    CancelFeeWithdrawalMonitor(reqtime,order_nonce);
                                    //Make the fee wallet availabe to trade again for maker                             
                                    UpdateWalletStatus(3,0);
                                    UpdateWalletStatus(cointype,0);
                                    con.open = false; //Close the validator 
                                }       
                            }
                        }
                    }
                    else if (method == "cn.validator_trade_reject")
                    { //Validator receives this from a trader
                        if (response == 0)
                        {
                            if (con.outgoing == false && con.contype == 4 && critical_node == true)
                            { //From validation node
                              //A trader request to cancel to the validator node
                              //Nothing happens as order closes automatically upon timeout
                                string order_nonce = js["cn.order_nonce"].ToString();
                                if (order_nonce.Equals(con.tn_connection_nonce) == false) { return; } //Has to match
                                int reqtime = Convert.ToInt32(js["cn.utctime"].ToString());
                                int is_maker = Convert.ToInt32(js["cn.is_maker"].ToString());
                                if (is_maker == 1)
                                {
                                    NebliDexNetLog("Maker is requesting a cancelation of trade: " + order_nonce);
                                }
                                else
                                {
                                    NebliDexNetLog("Taker is requesting trade cancelation: " + order_nonce);
                                }
                            }
                        }
                    }
                    else if (method == "cn.getndexfee")
                    {
                        if (response == 0)
                        {
                            if (con.version < protocol_min_version)
                            { //This function requires minimum version
                                con.open = false; return;
                            }
                            SendCNServerAction(con, 53, "");
                        }
                        else if (critical_node == false && con.contype == 3)
                        {
                            decimal fee = Convert.ToDecimal(js["cn.ndexfee"].ToString(), CultureInfo.InvariantCulture);
                            fee = Math.Round(fee / 2) * 2;
                            if (fee > 100) { fee = 100; }
                            if (fee < 2) { fee = 2; }
                            ndex_fee = fee; //Update the ndex fee
                        }
                    }
                    else if (method == "cn.getblockhelper_status")
                    {
                        if (response == 0)
                        {
                            //From the client who wants to connect as a rpc link
                            con.contype = 5; //Mark it as such
                            SendCNServerAction(con, 57, "");
                        }
                        //The client doesn't use the callback to process to these type of responses
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Error parsing CN message: " + msg + ", error: " + e.ToString());
                    con.open = false; //Close the connection for malformed messages
                }
            }
            else if (con.contype == 5)
            {
                //CN connection to client for RPC only purposes
                try
                {
                    if (using_blockhelper == false && con.outgoing == false)
                    {
                        throw new Exception("Unable to provide data. BlockHelper is off.");
                    } //Someone is querying us for blockhelper data but blockhelper is offline
                    if (con.outgoing == true)
                    {
                        //All type 5 connections use blocking calls, set the handle here
                        //Client received message from CN
                        con.blockdata = msg;
                        con.blockhandle.Set();
                        return;
                    }
                    JObject js = JObject.Parse(msg); //Parse this message
                    string response;
                    string method = js["cn.method"].ToString();
                    if (method == "blockhelper.gettransactioninfo")
                    {
                        //We are the server, the client wants us to grab transaction information for a transaction
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.gettransactioninfo";
                        req["rpc.response"] = 0;
                        string txhash = js["cn.txhash"].ToString();
                        //Simple input validation
                        if (txhash.Length > 100)
                        {
                            throw new Exception("Transaction hash format invalid");
                        }
                        if (txhash.IndexOf(" ") > -1)
                        {
                            throw new Exception("Transaction hash format invalid");
                        }
                        req["rpc.txhash"] = txhash;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0)
                        {
                            throw new Exception("Unable to provide data. BlockHelper is off.");
                        }
                        //Rename the responses
                        JObject result_template = JObject.Parse(response);
                        JObject result = new JObject();
                        result["cn.method"] = "blockhelper.gettransactioninfo";
                        result["cn.response"] = 1;
                        if (result_template["rpc.error"] != null)
                        {
                            result["cn.error"] = result_template["rpc.error"];
                        }
                        if (result_template["rpc.result"] != null)
                        {
                            result["cn.result"] = result_template["rpc.result"];
                        }
                        SendCNServerAction(con, 58, JsonConvert.SerializeObject(result));
                    }
                    else if (method == "blockhelper.getaddressinfo")
                    {
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.getaddressinfo";
                        req["rpc.response"] = 0;
                        string address = js["cn.address"].ToString();
                        //Simple input validation
                        if (address.Length > 100)
                        {
                            throw new Exception("Address format invalid");
                        }
                        if (address.IndexOf(" ") > -1)
                        {
                            throw new Exception("Address format invalid");
                        }
                        req["rpc.neb_address"] = address;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0)
                        {
                            throw new Exception("Unable to provide data. BlockHelper is off.");
                        }
                        //Rename the responses
                        JObject result_template = JObject.Parse(response);
                        JObject result = new JObject();
                        result["cn.method"] = "blockhelper.getaddressinfo";
                        result["cn.response"] = 1;
                        if (result_template["rpc.error"] != null)
                        {
                            result["cn.error"] = result_template["rpc.error"];
                        }
                        if (result_template["rpc.result"] != null)
                        {
                            result["cn.result"] = result_template["rpc.result"];
                        }
                        SendCNServerAction(con, 58, JsonConvert.SerializeObject(result));
                    }
                    else if (method == "blockhelper.broadcasttx")
                    {
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.broadcasttx";
                        req["rpc.response"] = 0;
                        string txhex = js["cn.tx_hex"].ToString();
                        //Simple input validation
                        if (txhex.IndexOf(" ") > -1)
                        {
                            throw new Exception("Hex format invalid");
                        }
                        req["rpc.tx_hex"] = txhex;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0)
                        {
                            throw new Exception("Unable to provide data. BlockHelper is off.");
                        }
                        //Rename the responses
                        JObject result_template = JObject.Parse(response);
                        JObject result = new JObject();
                        result["cn.method"] = "blockhelper.broadcasttx";
                        result["cn.response"] = 1;
                        if (result_template["rpc.error"] != null)
                        {
                            result["cn.error"] = result_template["rpc.error"];
                        }
                        if (result_template["rpc.result"] != null)
                        {
                            result["cn.result"] = result_template["rpc.result"];
                        }
                        SendCNServerAction(con, 58, JsonConvert.SerializeObject(result));
                    }
                    else if (method == "blockhelper.getblockheight")
                    {
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.getblockheight";
                        req["rpc.response"] = 0;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0)
                        {
                            throw new Exception("Unable to provide data. BlockHelper is off.");
                        }
                        //Rename the responses
                        JObject result_template = JObject.Parse(response);
                        JObject result = new JObject();
                        result["cn.method"] = "blockhelper.getblockheight";
                        result["cn.response"] = 1;
                        if (result_template["rpc.error"] != null)
                        {
                            result["cn.error"] = result_template["rpc.error"];
                        }
                        if (result_template["rpc.result"] != null)
                        {
                            result["cn.result"] = result_template["rpc.result"];
                        }
                        SendCNServerAction(con, 58, JsonConvert.SerializeObject(result));
                    }
                    else if (method == "blockhelper.getspentaddressinfo")
                    {
                        JObject req = new JObject();
                        req["rpc.method"] = "rpc.getspentaddressinfo";
                        req["rpc.response"] = 0;
                        string address = js["cn.address"].ToString();
                        //Simple input validation
                        if (address.Length > 100)
                        {
                            throw new Exception("Address format invalid");
                        }
                        if (address.IndexOf(" ") > -1)
                        {
                            throw new Exception("Address format invalid");
                        }
                        req["rpc.neb_address"] = address;
                        int max_utxo = Convert.ToInt32(js["cn.max_utxo"].ToString());
                        if (max_utxo > 1000 || max_utxo < 1)
                        {
                            throw new Exception("UTXO count invalid");
                        }
                        req["rpc.max_utxo"] = max_utxo;
                        response = SendBlockHelperMessage(req, true);
                        if (response.Length == 0)
                        {
                            throw new Exception("Unable to provide data. BlockHelper is off.");
                        }
                        //Rename the responses
                        JObject result_template = JObject.Parse(response);
                        JObject result = new JObject();
                        result["cn.method"] = "blockhelper.getspentaddressinfo";
                        result["cn.response"] = 1;
                        if (result_template["rpc.error"] != null)
                        {
                            result["cn.error"] = result_template["rpc.error"];
                        }
                        if (result_template["rpc.result"] != null)
                        {
                            result["cn.result"] = result_template["rpc.result"];
                        }
                        SendCNServerAction(con, 58, JsonConvert.SerializeObject(result));
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Error parsing CN message: " + msg + ", error: " + e.ToString());
                    con.open = false;
                }
            }
        }

        public static int NSReadLine(SslStream s, Byte[] b, int off, int amount)
        {
            //This function will read byte by byte until it gets to a new line (for electrum)
            byte mybyte = 0;
            int start = 0;
            byte[] tempbuf = new byte[1];
            while (start < amount)
            { //Newline
                int n = s.Read(tempbuf, 0, 1); //More efficient than readbyte
                if (n == 0)
                {
                    //Nothing more to read, should not happen normally
                    break;
                }
                mybyte = tempbuf[0];
                b[off] = mybyte;
                off++;
                start++;
                if (mybyte == 10) { start--; break; } //Break on newline
            }
            return start;
        }

        public static System.Object debugfileLock = new System.Object();
        public static void NebliDexNetLog(string msg)
        {
            System.Diagnostics.Debug.WriteLine(msg);
            //Also write this information to a log
            lock (debugfileLock)
            {
                try
                {
                    using (StreamWriter file = File.AppendText(App_Path + "/data/debug.log"))
                    {
                        string format_time = UTC2DateTime(UTCTime()).ToString("MM-dd:HH-mm-ss");
                        file.WriteLine(format_time + ": " + msg);
                    }
                }
                catch (Exception) { }
            }
        }

		public static bool RemoveElectrumServer(int type, string ip)
        {
            if (File.Exists(App_Path + "/data/electrum_peers.dat") == false)
            {
                NebliDexNetLog("All electrum servers are down, unable to connect");
                return false;
            }

            //Type -1 means delete IP address in all coin types

            lock (DexConnectionList)
            { //Wait till this list is available
                try
                {
                    using (System.IO.StreamReader file_in =
                        new System.IO.StreamReader(@App_Path + "/data/electrum_peers.dat", false))
                    {
                        using (System.IO.StreamWriter file_out =
                            new System.IO.StreamWriter(@App_Path + "/data/electrum_peers_new.dat", false))
                        {
                            int index = type;
                            List<string> server_info = new List<string>();
                            while (file_in.EndOfStream == false)
                            {
                                int num = Convert.ToInt32(file_in.ReadLine()); //Get number of nodes
                                int server_count = 0;
                                server_info.Clear();
                                for (int i = 0; i < num; i++)
                                {
                                    string ip_read = file_in.ReadLine();
                                    string port = file_in.ReadLine();
                                    if (ip_read.Equals(ip) == false)
                                    {
                                        server_info.Add(ip_read);
                                        server_info.Add(port);
                                        server_count++;
                                    }
                                    else
                                    {
                                        //This is a matching IP to remove
                                        if (index != 0 && type >= 0)
                                        {
                                            //But not from the same blockchaintype
                                            server_info.Add(ip_read);
                                            server_info.Add(port);
                                            server_count++;
                                        }
                                        else if (type >= 0)
                                        {
                                            ip = ""; //Only remove an IP address once
                                        }
                                    }
                                }

                                file_out.WriteLine(server_count.ToString());
                                if (server_count == 0)
                                {
                                    //No more nodes left for blockchain type
                                    file_out.Close();
                                    file_in.Close();
                                    File.Delete(App_Path + "/data/electrum_peers_new.dat");
                                    File.Delete(App_Path + "/data/electrum_peers.dat");
                                    NebliDexNetLog("Blockchain " + type + " missing nodes, must redownload nodes, unable to connect");
                                    return false; //Must re-download all nodes for all blockchain types
                                }

                                //Now write out the servers information
                                for (int i = 0; i < server_count; i++)
                                {
                                    file_out.WriteLine(server_info[i * 2]);
                                    file_out.WriteLine(server_info[i * 2 + 1]);
                                }
                                index--;
                            }
                        }
                    }

                    //Move the files around so we don't store anything in memory
                    if (File.Exists(App_Path + "/data/electrum_peers_new.dat") != false)
                    {
                        File.Delete(App_Path + "/data/electrum_peers.dat");
                        File.Move(App_Path + "/data/electrum_peers_new.dat", App_Path + "/data/electrum_peers.dat");
                    }
                }
                catch (Exception)
                {
                    NebliDexNetLog("Failed to write the new electrum_peers.dat");
                    return false;
                }
            }

            return true;
        }

        public static void RecreateCNList()
        {
            //Write out all the CN data
            lock (CN_Nodes_By_IP)
            { //Make this code thread safe
                try
                {
                    using (System.IO.StreamWriter file_out =
                        new System.IO.StreamWriter(@App_Path + "/data/cn_list_new.dat", false))
                    {
                        file_out.WriteLine("" + DNS_SEED_TYPE);
                        file_out.WriteLine(DNS_SEED);
                        foreach (string iprow in CN_Nodes_By_IP.Keys)
                        {
                            file_out.WriteLine(iprow);
                        }
                    }

                    //Move the files around so we don't store anything in memory
                    if (File.Exists(App_Path + "/data/cn_list_new.dat") != false)
                    {
                        if (File.Exists(App_Path + "/data/cn_list.dat") == true)
                        {
                            File.Delete(App_Path + "/data/cn_list.dat");
                        }
                        File.Move(App_Path + "/data/cn_list_new.dat", App_Path + "/data/cn_list.dat");
                    }
                }
                catch (Exception)
                {
                    NebliDexNetLog("Failed to write new cn_list.dat");
                }
            }

            return;
        }

        public static void LoadCNList()
        {
            //This function will load the CN list and add ones that haven't already been loaded
            lock (CN_Nodes_By_IP)
            {
				try
				{
					using (System.IO.StreamReader file =
						new System.IO.StreamReader(@App_Path + "/data/cn_list.dat", false))
					{
						DNS_SEED_TYPE = Convert.ToInt32(file.ReadLine());
						DNS_SEED = file.ReadLine();
						while (file.EndOfStream == false)
						{
							CriticalNode cn = new CriticalNode();
							cn.ip_add = file.ReadLine();
							if (cn.ip_add.Length == 0) { break; } //Empty new line
							if (CN_Nodes_By_IP.ContainsKey(cn.ip_add) == false)
							{
								//Only add nodes that don't already exist
								CN_Nodes_By_IP.Add(cn.ip_add, cn); //Add this dictionary entry
							}
						}
					}
				}catch(Exception e){
					NebliDexNetLog("Failed to load CN List: " + e.ToString());
                    File.Delete(@App_Path + "/data/cn_list.dat"); //Delete then try to reload again
				}
            }
        }

        public static bool SelectRandomElectrum(int type, string[] prop)
        {
            //This will load the electrum peers and find a random server to connect to
            if (File.Exists(App_Path + "/data/electrum_peers.dat") == false)
            {
                return false;
            }

            using (System.IO.StreamReader file =
                new System.IO.StreamReader(@App_Path + "/data/electrum_peers.dat", false))
            {
                int index = type;
                while (file.EndOfStream == false)
                {
                    int num = Convert.ToInt32(file.ReadLine()); //Get number of nodes
                    int selected = 0;
                    if (index == 0)
                    {
                        //Get a random number to select the node
                        selected = (int)Math.Round(GetRandomNumber(1, num)) - 1;
                    }
                    for (int i = 0; i < num; i++)
                    {
                        if (index == 0 && selected == i)
                        {
                            //This is our node
                            prop[0] = file.ReadLine();
                            prop[1] = file.ReadLine();
                            return true;
                        }
                        else
                        {
                            file.ReadLine(); //IP address
                            file.ReadLine(); //Port
                        }
                    }
                    index--;
                }
            }

			File.Delete(App_Path + "/data/electrum_peers.dat"); //Unable to find a server to connect to, re-download whole list
            return false;
        }

        //Query Servers periodically
        public static int TransactionQueryCounter = 10; //A counter based on five seconds periodicquery
        public static bool PeriodQueryOpen = false; //This is flagged when the node is still trying to connect to a node
        public static bool PeriodicLongRunnersOpen = false;
        public static int LastNetworkQueryTime = -1;

        public static void PeriodicNetworkQuery(object state)
        {
            if (PeriodQueryOpen == true) { return; } //This is already running elsewhere
            PeriodQueryOpen = true;

            try
            {

                //Check for clock tampering
                if (critical_node == true)
                {
                    int cn_time = UTCTime();
                    if (LastNetworkQueryTime < 0)
                    {
                        LastNetworkQueryTime = cn_time;
                    }
                    else
                    {
                        if (cn_time - LastNetworkQueryTime > 30)
                        {
                            //This shouldn't happen under normal circumstances
                            NebliDexNetLog("System clock is out of sync, attempting to reconnect as critical node");
                            reconnect_cn = true;
                        }
                        LastNetworkQueryTime = cn_time;
                    }
                }

                string my_ip = "";
                if (critical_node == true)
                {
                    my_ip = getPublicFacingIP();
                }
                //Go through all of the electrum servers and cn servers and make sure to keep sending keep alives

                //This function also removes closed dex connections and dex connerection requests
                //Lists are not thread safe

				//Check old recent trades and remove them (>24 hours)
                for (int i = 0; i < total_markets; i++)
                {
                    lock (RecentTradeList[i])
                    {
                        for (int pos = RecentTradeList[i].Count - 1; pos >= 0; pos--)
                        {
                            if (UTCTime() - RecentTradeList[i][pos].utctime > 60 * 60 * 24)
                            { //Remove after 24 hours
                                RecentTrade rt = RecentTradeList[i][pos];
                                if (main_window_loaded == true && exchange_market == rt.market)
                                {
                                    //Remove the recent trades shown on the list
                                    Application.Invoke(delegate
                                    {
                                        main_window.Recent_Trade_List_Public.NodeStore.RemoveNode(rt);
                                    });
                                }
                                RecentTradeList[i].RemoveAt(pos);
                            }
                        }
                    }
                }

                //Check cancellation tokens
                lock (CancelOrderTokenList)
                {
                    for (int i = CancelOrderTokenList.Count - 1; i >= 0; i--)
                    {
                        if (UTCTime() - CancelOrderTokenList[i].arrivetime > 60 * 5)
                        {
                            //This cancellation token has been chilling for 5 minutes, remove it
                            CancelOrderTokenList.RemoveAt(i); //Remove and move on
                        }
                    }
                }

                //Remove inactive/closed streams
                //This is not thread safe so must lock it
                lock (DexConnectionList)
                {
                    for (int i = DexConnectionList.Count - 1; i >= 0; i--)
                    {
                        DexConnection mydex = DexConnectionList[i];
                        if (mydex.open == false)
                        {

                            //Remove any orders linked to this connection as well if incoming connection
                            if (mydex.outgoing == false && mydex.version >= protocol_min_version && mydex.contype == 3)
                            {
                                for (int market = 0; market < total_markets; market++)
                                {
                                    lock (OpenOrderList[market])
                                    {
                                        for (int pos = 0; pos < OpenOrderList[market].Count; pos++)
                                        {
                                            if (OpenOrderList[market][pos].cn_relayer_ip.Equals(my_ip) == true)
                                            {
                                                //These are my orders
                                                if (OpenOrderList[market][pos].ip_address_port[0] == mydex.ip_address[0] && OpenOrderList[market][pos].ip_address_port[1] == mydex.ip_address[1])
                                                {
                                                    //Mark the order for deletion
                                                    OpenOrderList[market][pos].deletequeue = true;
                                                }
                                            }
                                        }
                                    }
                                }

                            }
                            else if (mydex.outgoing == true && mydex.contype == 3)
                            {
                                //This is our outgoing connection, queue all our maker orders if lost connection detected
                                //Remove any order                              
                                if (MyOpenOrderList.Count > 0)
                                {
                                    QueueAllOpenOrders();
                                }
                            }

                            if (mydex.outgoing == false)
                            { //Inbound connection
                                if (TN_Connections.ContainsKey(mydex.ip_address[0]) == true)
                                {
                                    lock (TN_Connections)
                                    {
                                        TN_Connections[mydex.ip_address[0]]--;
                                        if (TN_Connections[mydex.ip_address[0]] <= 0)
                                        {
                                            TN_Connections.Remove(mydex.ip_address[0]); //Take it out the list
                                        }
                                    }
                                }
                            }

                            //For connections that should have closed
                            if ((mydex.version >= protocol_min_version && mydex.contype == 3) || mydex.contype == 4)
                            {
                                string nonce_info = "";
                                if (mydex.tn_connection_nonce.Length > 0)
                                {
                                    nonce_info = " Nonce: " + mydex.tn_connection_nonce;
                                }
                                if (mydex.outgoing == true)
                                {
                                    NebliDexNetLog("Closing outgoing CN connection (" + mydex.ip_address[0] + "): Type: " + mydex.contype + nonce_info);
                                }
                                else
                                {
                                    NebliDexNetLog("Closing inbound CN connection (" + mydex.ip_address[0] + "): Type: " + mydex.contype + nonce_info);
                                    //We are going to check if this is a critical node IP that is disconnecting from us (May not be a CN itself)
                                    if (CheckIfCN(mydex) == true)
                                    {
                                        CN_Nodes_By_IP[mydex.ip_address[0]].lastchecked = UTCTime() - 16 * 60; //Mark this CN to be checked in case it did disconnect
                                    }
                                }
                            }

                            if (mydex.contype == 0)
                            {
                                //Couldn't connect to blockhelper anymore, use the API instead
                                using_blockhelper = false;
                            }
                            else if (mydex.contype == 5)
                            {
                                if (mydex.outgoing == true)
                                {
                                    //Lost connection to the CN blockhelper
                                    using_cnblockhelper = false;
                                }
                            }


                            mydex.closeConnection();
                            DexConnectionList.RemoveAt(i);
                        }
                    }
                }

                //Remove connection requests from those streams and others
                lock (DexConnectionReqList)
                {
                    for (int i = DexConnectionReqList.Count - 1; i >= 0; i--)
                    {
                        if (DexConnectionReqList[i].delete == true)
                        {
                            DexConnectionReqList.RemoveAt(i);
                        }
                        else
                        {
                            //Remove connections for requests that haven't been responded to
                            if (UTCTime() - DexConnectionReqList[i].creation_time > 15)
                            {
                                NebliDexNetLog("Electrum node failed to respond, closing connection");
                                DexConnectionReqList[i].electrum_con.open = false;
                                DexConnectionReqList[i].delete = true;
                            }
                        }
                    }
                }

                //Remove open orders that have been marked pending for a long time (for housekeeping purposes)
                //All nodes will do it at the same time, so no need to relay it
                for (int market = 0; market < total_markets; market++)
                {
                    lock (OpenOrderList[market])
                    {
                        for (int pos = OpenOrderList[market].Count - 1; pos >= 0; pos--)
                        {
                            if (OpenOrderList[market][pos].order_stage > 0)
                            {
                                if (UTCTime() - OpenOrderList[market][pos].pendtime > max_transaction_wait)
                                { //Pended for over 3 hours
                                    OpenOrder ord = OpenOrderList[market][pos];
                                    OpenOrderList[market].RemoveAt(pos);
                                    if (main_window_loaded == true)
                                    {
                                        main_window.RemoveOrderFromView(ord);
                                    }
                                }
                            }
                        }
                    }
                }

                //Remove open orders that are marked for deletion (CN function only)
                if (critical_node == true)
                {
                    for (int market = 0; market < total_markets; market++)
                    {
                        lock (OpenOrderList[market])
                        {
                            for (int pos = OpenOrderList[market].Count - 1; pos >= 0; pos--)
                            {
                                if (OpenOrderList[market][pos].deletequeue == true)
                                {
                                    OpenOrder ord = OpenOrderList[market][pos];
                                    OpenOrderList[market].RemoveAt(pos);
                                    CNCancelOrder(ord);
                                }
                            }
                        }
                    }

                    //Check open order requests
                    lock (OrderRequestList)
                    {
                        for (int i = OrderRequestList.Count - 1; i >= 0; i--)
                        {
                            if (UTCTime() - OrderRequestList[i].utctime > 60 * 60 * 1)
                            {
                                //Remove this order request if its been around more than 1 hrs
                                //Order should be complete by then (meaning has validating transaction available)
                                OrderRequestList.RemoveAt(i); //Remove and move on
                            }
                            else if (OrderRequestList[i].order_stage < 3)
                            { //Not closed request
                                int limit = 60;
                                if (OrderRequestList[i].order_stage > 0) { limit = 180; } //The validation failed to complete, give it 3 minutes
                                if (UTCTime() - OrderRequestList[i].utctime > limit)
                                { //Request has been unresponded too for too long
                                  //This request needs to be removed and corresponding dex notified
                                    CloseFailedOrderRequest(OrderRequestList[i]);
                                    OrderRequestList.RemoveAt(i);
                                }
                            }
                        }
                    }

                }

                int ctime = UTCTime();
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        DexConnection mydex = DexConnectionList[i];

                        try
                        {
                            if (mydex.contype == 1)
                            {
                                if (ctime - mydex.lasttouchtime > 50)
                                { //Send keep alive every 50 seconds
                                  //Send the Electrum Keep Alive, a ping
                                    SendElectrumAction(mydex, 6, null); //Keep Alive ping
                                }

                                //Now get the balance for each wallet
                                SendElectrumAction(mydex, 2, null); //Get balance of coins
								if (mydex.blockchain_type != 4)
                                {
                                    // Bitcoin Cash estimatefee doesn't work
                                    // Current implementation of ElectrumX doesn't allow for estimatefee without arguments
                                    SendElectrumAction(mydex, 8, null); //Get estimated fee as well
                                }
                            }
                            else if (mydex.contype > 2)
                            {
                                if (mydex.outgoing == true)
                                {
                                    if (ctime - mydex.lasttouchtime > 50)
                                    { //Send keep alive every 50 seconds
                                      //Send CN Ping
                                        SendCNServerAction(mydex, 16, ""); //Keep Alive ping, with pong response
                                    }
                                }
                                else if (mydex.outgoing == false)
                                {
                                    //Critical Node connection to Trader Node
                                    if (ctime - mydex.lasttouchtime > 90)
                                    { //Cut off connection after 90 seconds of no signal
                                        NebliDexNetLog("Server: Cut Dead Connection");
                                        mydex.open = false;
                                    }

                                    if (mydex.version == 0 && mydex.contype == 3)
                                    {
                                        //For short connections for data querying
                                        if (ctime - mydex.lasttouchtime > 20)
                                        { //Cut off connection after 20 seconds
                                            NebliDexNetLog("Server: Cut Temporary Connection");
                                            mydex.open = false;
                                        }
                                    }
                                }

                                //This code will cut a connection that is spamming the node
                                if (mydex.msg_count > 25 + total_markets)
                                {
                                    if (CheckIfCN(mydex) == false)
                                    {
                                        //Not a CN connection so not allowed to send many messages
                                        NebliDexNetLog("This connection is spamming/flooding your node, dropping it");
                                        mydex.open = false;
                                    }
                                }
                                mydex.msg_count = 0;
                            }
                            else if (mydex.contype == 0)
                            {
                                if (mydex.outgoing == true)
                                {
                                    if (ctime - mydex.lasttouchtime > 70)
                                    { //Send keep alive every 70 seconds
                                      //Send Ping to BlockHelper if needed
                                        JObject req = new JObject();
                                        req["rpc.method"] = "rpc.serverversion";
                                        req["rpc.response"] = 0;
                                        SendBlockHelperMessage(req, true);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            NebliDexNetLog("Failed to connect: " + mydex.ip_address[0] + ", error: " + e.ToString());
                            //Now remove the connection
                            mydex.open = false;
                        }
                    }
                }

                TransactionQueryCounter += 5;
                if (PeriodicLongRunnersOpen == false)
                {
                    Task.Run(() => PeriodicLongRunners());
                }


                //Update the status bar
                if (main_window_loaded == true)
                {
					Application.Invoke(delegate
                    {
                        main_window.UpdateBlockrates();
                    });
                }

            }
            catch (Exception e)
            {
                NebliDexNetLog("Periodic Network Query Exception: " + e.ToString());
            }
            finally
            {
                PeriodQueryOpen = false; //Let the system know this is available to run again
            }

        }

        public static void PeriodicLongRunners()
        {
            PeriodicLongRunnersOpen = true;

            try
            {
                //This function contains methods that can run longer than 5 seconds
                //It will at least be ran every 5 seconds
                //There can only be once instance of this open

                //Get Balances for Neblio and Tokens
                GetNTP1Balances();

				//Get Ethereum Blockchain Fees and Balance
                GetEthereumWalletBalances();
                GetEthereumBlockchainFee();
            
                if (critical_node == true)
                {
                    //Query the CNs for connectivity
                    CheckRandomCNBalance();
                }

                //Now check transactions every 10 seconds for changes on blockchain
                if (TransactionQueryCounter >= 10)
                {
                    try
                    {
                        TransactionQueryCounter = 0;
                        //Now run a transaction checker
                        CheckMyTransactions();
                        if (critical_node == true)
                        {
                            CheckValidatingTransactions(); //Checks Transactions that are being validated
                            if (reconnect_cn == true)
                            {
                                //Try to rebroadcast CN status
                                RebroadcastCNStatus();
                            }
                        }
                        PruneDatabases();
                    }
                    catch (Exception e)
                    {
                        NebliDexNetLog("Failed to modify databases, error: " + e.ToString());
                    }
                }

                //Reconnect to broken Dex connections
				//First reconnect broken electrum nodes 
                bool dex_exist = false;
                for (int i = 1; i < total_cointypes; i++)
                {
                    dex_exist = false;
                    if (i == 6) { continue; } //Ethereum doesn't have electrum
                    lock (DexConnectionList)
                    {
                        for (int i2 = 0; i2 < DexConnectionList.Count; i2++)
                        {
                            if (DexConnectionList[i2].contype == 1 && DexConnectionList[i2].outgoing == true && DexConnectionList[i2].open == true && DexConnectionList[i2].blockchain_type == i)
                            {
                                dex_exist = true; break; //There is a connection for this electrum server
                            }
                        }
                    }
                    if (dex_exist == false)
                    {
                        //Connection was lost
                        //This specific blockchain type is not there
                        NebliDexNetLog("Please reconnect electrum blocktype: " + i);
                        ConnectElectrumServers(i);
                    }
                }

				//Now go through CN connections and reconnect if necessary
                dex_exist = false;
                lock (DexConnectionList)
                {
                    for (int i2 = 0; i2 < DexConnectionList.Count; i2++)
                    {
                        if (DexConnectionList[i2].contype == 3 && DexConnectionList[i2].outgoing == true && DexConnectionList[i2].open == true)
                        {
                            dex_exist = true; break; //There is a connection for this electrum server
                        }
                    }
                }
				if (dex_exist == false)
                {
                    if (critical_node == false)
                    {
                        ConnectCNServer(false); //Always maintain a connection to another CN
                    }
                    else
                    {
                        if (CN_Nodes_By_IP.Count > 1)
                        {
                            //There is more than one node on network
                            ConnectCNServer(false); //Always maintain a connection to another CN
                        }
                        else if (CN_Nodes_By_IP.Count == 0)
                        {
							//Should not happen, disconnect the CN
                            ToggleCriticalNodeServer(false); //Turn server back off
                            if (run_headless == false)
                            {
                                Application.Invoke(delegate
                                {
                                    if (main_window_loaded == true)
                                    {
                                        MessageBox(main_window, "Notice", "Critical node out of sync. Must manually reactivate node.", "OK");
                                        main_window.ToggleCNInfo(false);
                                    }
                                });
                            }
                            else
                            {
                                Console.WriteLine("Critical node out of sync. Must manually reactivate node. Closing Program");
                                Headless_Application_Close();
                            }
                        }
                    }
                }

                CheckMyQueuedOrders(); //Check orders that have been queued and repost them if necessary                
            }
            catch (Exception e)
            {
                NebliDexNetLog("Periodic Long Runner Error: " + e.ToString());
            }
            finally
            {
                PeriodicLongRunnersOpen = false;
            }

        }

        public static void CheckMyQueuedOrders()
        {
            //Go through each open order and check if queued
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

            if (dex == null) { return; } //No dex connections could be found, try again later

            if (ntp1downcounter > 1) { return; } //Do not try to repost Order while the API server is down

            lock (MyOpenOrderList)
            {
                for (int i = MyOpenOrderList.Count - 1; i >= 0; i--)
                {
                    if (MyOpenOrderList[i].queued_order == true)
                    {
                        if (UTCTime() - MyOpenOrderList[i].pendtime > 30)
                        {
                            //More than 30 seconds has passed
                            //Check conditions for rebroadcasting
                            MyOpenOrderList[i].pendtime = UTCTime();

                            //First check to make sure that there are no open orders in a trade currently
                            for (int i2 = 0; i2 < MyOpenOrderList.Count; i2++)
                            {
                                if (MyOpenOrderList[i2].order_stage > 0) { return; }
                                if (MyOpenOrderList[i2].is_request == true) { return; } //Wait until taker order is finished
                                                                                        //Order actively in a trade, can't close it
                            }

                            //Next check to make sure the wallet is available (not in wait)
                            //We will wait 
                            bool useable = false;
                            string msg = "";
                            if (MyOpenOrderList[i].type == 0)
                            {
                                //This is a buy order we are making, so we need base market balance
                                int wallet = MarketList[MyOpenOrderList[i].market].base_wallet;
                                useable = CheckWalletBalance(wallet, 0, ref msg);
                                //New code
                                if (useable == true)
                                {
                                    if (MarketList[MyOpenOrderList[i].market].trade_wallet != 3)
                                    {
                                        //Not buying NDEX, so we need NDEX to trade, check the NDEX wallet and see if available
                                        useable = CheckWalletBalance(3, 0, ref msg);
                                    }
                                }
                            }
                            else
                            {
                                //Selling the trade wallet amount
                                int wallet = MarketList[MyOpenOrderList[i].market].trade_wallet;
                                useable = CheckWalletBalance(wallet, 0, ref msg);
                            }

                            if (useable == false) { continue; } //Skip this queued order

                            //So it is usuable, so check to make sure the wallet can handle the order otherwise close it

                            //Now run it against normal checks
                            bool remove_order = false;
                            decimal total = Math.Round(MyOpenOrderList[i].price * MyOpenOrderList[i].amount, 8);

                            if (MarketList[MyOpenOrderList[i].market].base_wallet == 3 || MarketList[MyOpenOrderList[i].market].trade_wallet == 3)
                            {
                                //Make sure amount is greater than ndexfee x 2
                                if (MyOpenOrderList[i].amount < ndex_fee * 2)
                                {
                                    remove_order = true;
                                }
                            }

                            bool good = false;
                            if (MyOpenOrderList[i].type == 0)
                            {
                                //This is a buy order we are making, so we need base market balance
                                int wallet = MarketList[MyOpenOrderList[i].market].base_wallet;
                                good = CheckWalletBalance(wallet, total, ref msg);
                                if (good == true)
                                {
                                    //Now check the fees
                                    good = CheckMarketFees(MyOpenOrderList[i].market, MyOpenOrderList[i].type, total, ref msg, false);
                                }
                            }
                            else
                            {
                                //Selling the trade wallet amount
                                int wallet = MarketList[MyOpenOrderList[i].market].trade_wallet;
                                good = CheckWalletBalance(wallet, MyOpenOrderList[i].amount, ref msg);
                                if (good == true)
                                {
                                    good = CheckMarketFees(MyOpenOrderList[i].market, MyOpenOrderList[i].type, MyOpenOrderList[i].amount, ref msg, false);
                                }
                            }

                            if (good == false) { remove_order = true; }

							//Make sure that total is greater than block rates for both markets
                            decimal block_fee1 = 0;
                            decimal block_fee2 = 0;
                            int trade_wallet_blockchaintype = GetWalletBlockchainType(MarketList[exchange_market].trade_wallet);
                            int base_wallet_blockchaintype = GetWalletBlockchainType(MarketList[exchange_market].base_wallet);
                            block_fee1 = blockchain_fee[trade_wallet_blockchaintype];
                            block_fee2 = blockchain_fee[base_wallet_blockchaintype];

                            //Now calculate the totals for ethereum blockchain
                            if (trade_wallet_blockchaintype == 6)
                            {
								block_fee1 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[exchange_market].trade_wallet));
								if(Wallet.CoinERC20(MarketList[exchange_market].trade_wallet) == true){
                                    block_fee1 = Convert.ToDecimal(double_epsilon); // The minimum trade size for ERC20 tokens
                                }  
                            }
                            if (base_wallet_blockchaintype == 6)
                            {
								block_fee2 = GetEtherContractTradeFee(Wallet.CoinERC20(MarketList[exchange_market].base_wallet));
								if(Wallet.CoinERC20(MarketList[exchange_market].base_wallet) == true){
                                    block_fee2 = Convert.ToDecimal(double_epsilon); // The minimum trade size for ERC20 tokens
                                }  
                            }

                            if (total < block_fee2 || MyOpenOrderList[i].amount < block_fee1)
                            {
                                //Smaller than blockchain fees
                                remove_order = true;
                            }
							                     
	                        //ERC20 only check
                            bool sending_erc20 = false;
                            decimal erc20_amount = 0;
                            int erc20_wallet = 0;
                            if (MyOpenOrderList[i].type == 0 && Wallet.CoinERC20(App.MarketList[App.exchange_market].base_wallet) == true)
                            {
                                //Buying trade with ERC20
                                sending_erc20 = true;
                                erc20_amount = total;
                                erc20_wallet = MarketList[exchange_market].base_wallet;
                            }
                            else if (MyOpenOrderList[i].type == 1 && Wallet.CoinERC20(App.MarketList[App.exchange_market].trade_wallet) == true)
                            {
                                //Selling trade that is also an ERC20
                                sending_erc20 = true;
                                erc20_amount = MyOpenOrderList[i].amount;
                                erc20_wallet = MarketList[exchange_market].trade_wallet;
                            }

                            if (sending_erc20 == true)
                            {
                                //Make sure the allowance is there already
                                decimal allowance = App.GetERC20AtomicSwapAllowance(App.GetWalletAddress(erc20_wallet), App.ERC20_ATOMICSWAP_ADDRESS, erc20_wallet);
                                if (allowance < 0)
                                {
                                    NebliDexNetLog("Error determining ERC20 contract allowance");
                                    continue;
                                }
                                else if (allowance < erc20_amount)
                                {
                                    //We need to increase the allowance to send to the atomic swap contract eventually
                                    //Since we can't post order, just remove it
                                    remove_order = true;
                                }
                            }

                            RemoveSavedOrder(MyOpenOrderList[i]); //Remove all the saved orders, will resave them with new nonces

                            if (remove_order == true)
                            {
								OpenOrder myord = MyOpenOrderList[i];
                                MyOpenOrderList.RemoveAt(i);
                                NebliDexNetLog("Closed queued order due to changed wallet state");
								Application.Invoke(delegate
                                {
                                    if (main_window_loaded == true)
                                    {
                                        main_window.Open_Order_List_Public.NodeStore.RemoveNode(myord);
                                    }
                                });
                            }
                            else
                            {
                                //Create another order nonce and then resubmit order
                                MyOpenOrderList[i].order_stage = 0;
                                MyOpenOrderList[i].queued_order = false; //Not queued anymore
                                MyOpenOrderList[i].order_nonce = GenerateHexNonce(32);
                                decimal original_a = MyOpenOrderList[i].original_amount;
                                MyOpenOrderList[i].original_amount = MyOpenOrderList[i].amount;
                                if (MyOpenOrderList[i].minimum_amount > MyOpenOrderList[i].original_amount)
                                {
                                    MyOpenOrderList[i].minimum_amount = MyOpenOrderList[i].original_amount;
                                    //This makes sure the minimum amount is modified before submitting otherwise it will be rejected
                                }

                                bool worked = SubmitMyOrder(MyOpenOrderList[i], dex);
                                MyOpenOrderList[i].original_amount = original_a; //Reset original amount
                                if (worked == true)
                                {
                                    //Do this on the UI thread
                                    OpenOrder myord = MyOpenOrderList[i];

                                    AddSavedOrder(myord); //Save the new posted order with the new nonce

									Application.Invoke(delegate
                                    {
                                        lock (OpenOrderList[myord.market])
                                        {
                                            OpenOrderList[myord.market].Add(myord);
                                        }
                                        if (main_window_loaded == true)
                                        {
                                            main_window.AddOrderToView(myord);
                                            main_window.Open_Order_List_Public.QueueDraw(); //Force redraw of the list
                                        }
                                    });
                                }
                                else
                                {
									//Order did not post successfully
                                    OpenOrder myord = MyOpenOrderList[i];
                                    MyOpenOrderList.RemoveAt(i);
                                    NebliDexNetLog("Closed queued order due to rejection by critical node.");
                                    Application.Invoke(delegate
                                    {
                                        if (main_window_loaded == true)
                                        {
                                            main_window.Open_Order_List_Public.NodeStore.RemoveNode(myord);
                                        }
                                    });
                                }
                            }

                        }
                    }
                }
            }
        }

        public static void SendElectrumAction(DexConnection con, int action, string blockdata)
        {
            //Make sure this is only accessed by one thread
            lock (con)
            {
                con.electrum_id++;
                if (con.electrum_id >= uint.MaxValue) { con.electrum_id = 0; } //Start over the counter

                //Add this request to the request queue
                DexConnectionReq req = new DexConnectionReq();
                req.electrum_con = con;
                req.electrum_id = con.electrum_id;
                req.requesttype = action;
                req.creation_time = UTCTime();
                DexConnectionReqList.Add(req);

                string json_encoded = "";

                if (action == 1)
                {
                    //Ping
                    //Create the JSON
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "server.version";
                    js["params"] = new JArray("NebliDex", "1.4"); //Latest electrum version
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 2)
                {
                    //Get the balance for the wallet
                    string scripthash = "";

                    for (int i = 0; i < WalletList.Count; i++)
                    {
                        if (WalletList[i].blockchaintype == con.blockchain_type)
                        {
                            string wal_add = WalletList[i].address;
							scripthash = GetElectrumScriptHash(wal_add, WalletList[i].type);
                            break;
                        }
                    }

                    //Now send this request to the electrum server
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.scripthash.get_balance";
                    js["params"] = new JArray(scripthash);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 3)
                {
                    //Get list of unspent transactions (Does not work for Neblio addresses with tokens)
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.scripthash.listunspent";
                    js["params"] = new JArray(blockdata); //Address
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 4)
                {
                    //Get raw transaction hex
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.transaction.get";
                    js["params"] = new JArray(blockdata, true); //Transaction hash with metadata
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 5)
                {
                    //Submit raw transaction hex to blockchain
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.transaction.broadcast";
                    js["params"] = new JArray(blockdata); //Raw transsaction
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 6)
                {
                    //This has become a ping request
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "server.ping";
                    js["params"] = new JArray();
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 7)
                {
                    //Get all address history, this will be a blocking call
                    //Used to retrieve secret from address
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.scripthash.get_history";
                    js["params"] = new JArray(blockdata); //Address scripthash
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 8)
                {
                    //Get the estimated fee (doesn't work for neblio)
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.estimatefee";
                    js["params"] = new JArray(1);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 9)
                {
                    //Get address balance, this will be a blocking call
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.scripthash.get_balance";
                    js["params"] = new JArray(blockdata);
                    json_encoded = JsonConvert.SerializeObject(js);
                }
                else if (action == 10)
                {
                    //Get current block header, this includes height, this will be a blocking call
                    //This is a subscription call, we will ignore the subscription result
                    con.blockdata = "";
                    JObject js = new JObject();
                    js["id"] = con.electrum_id;
                    js["method"] = "blockchain.headers.subscribe";
                    js["params"] = new JArray();
                    json_encoded = JsonConvert.SerializeObject(js);
                }

                if (json_encoded.Length > 0)
                {
                    try
                    {
                        Byte[] data = System.Text.Encoding.ASCII.GetBytes(json_encoded + "\r\n");
						con.secure_stream.Write(data, 0, data.Length);
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

		public static string HttpRequest(string url, string postdata, out bool timeout)
        {
            int request_time = UTCTime();
            string responseString = "";
            timeout = false;

            while (UTCTime() - request_time < 10)
            {
                timeout = false;
                if (http_open_network == false) { return ""; }

                try
                {
                    //This will make a request and return the result
                    //10 second timeout
                    //Post data must be formatted: var=something&var2=something

                    //This prevents server changes from breaking the protocol
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                    //url = "https://httpstat.us/200?sleep=3000";
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Credentials = System.Net.CredentialCache.DefaultCredentials;
                    request.Timeout = 10000; //Wait ten seconds

                    if (postdata.Length > 0)
                    { //It is a post request

                        byte[] data = Encoding.ASCII.GetBytes(postdata);

                        request.Method = "POST";
                        request.ContentType = "application/json";
						if (postdata.IndexOf("&",StringComparison.InvariantCulture) >= 0)
                        {
                            //Old school content type
                            request.ContentType = "application/x-www-form-urlencoded";
                        }
                        request.ContentLength = data.Length;

                        using (var stream = request.GetRequestStream())
                        {
                            stream.Write(data, 0, data.Length); //Write post data                     
                        }
                    }

                    //Mono has a bug that returns an exception in a task that cannot be caught
                    //We will ignore the exception returned due to the fact that we cannot prevent it

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        using (StreamReader respreader = new StreamReader(response.GetResponseStream()))
                        {
                            responseString = respreader.ReadToEnd();
                        }

                        break; //Leave the while loop, we got a good response
                    }
                }
                catch (WebException e)
                {
                    if (e.Status == WebExceptionStatus.Timeout)
                    {
                        //The server is offline or computer is not connected to internet
                        timeout = true;
                        NebliDexNetLog("The Server is offline or the computer is not connected to internet");
                    }
                    else
                    {
                        //Get the error and write the error to our log
                        if (e.Response != null)
                        {
                            StreamReader respreader = new StreamReader(e.Response.GetResponseStream());
                            string err_string = respreader.ReadToEnd();
                            respreader.Close();
                            e.Response.Close();
                            NebliDexNetLog("Request error for: " + url + ", error:\n" + err_string);
                        }
                        else
                        {
                            timeout = true;
                            NebliDexNetLog("Connection error for: " + url + ", error:\n" + e.ToString()); //No stream available
                        }
                    }
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Request error for: " + url + ", error:\n" + e.ToString());
                }

                if (timeout == false)
                {
                    NebliDexNetLog("Retrying request for: " + url + " due to error");
                }
                else
                {
                    break; //We won't retry because timeout was reached
                }
                Thread.Sleep(1000); //Wait one second to try again
            }
            return responseString;
        }

        public static Byte[] Uint2Bytes(uint myint)
        {
            //Converts the int to bytes and make sure its little endian
            Byte[] mybyte = BitConverter.GetBytes(myint);
            if (BitConverter.IsLittleEndian == false)
            {
                //Convert it to little endian
                Array.Reverse(mybyte);
            }
            return mybyte;
        }

        public static uint Bytes2Uint(Byte[] mybyte)
        {
            //Convert the 4 bytes into the uint
            if (BitConverter.IsLittleEndian == false)
            {
                //Convert it to big endian before converting
                Array.Reverse(mybyte);
            }
            uint myint = BitConverter.ToUInt32(mybyte, 0);
            return myint;
        }

        public static bool CheckIfCN(DexConnection con)
        {
            //Check to make sure this dexconnection is a critical node
            //For important methods like validation, spoof preventing methods will be carried out
            //Matching IP address to signature based on public key that matches balance required for cn
            return CN_Nodes_By_IP.ContainsKey(con.ip_address[0]);
        }

        public static void GetCNMarketData(int market)
        {
            //This function is for trader nodes / non-CN specific
            //It is used to get chartdata, open orders & recent orders for a specific market
            //The CN specific function will need to acquire open orders, recent orders, chart data for all markets
            //The CN specific function will also need order requests, all chartlastprices, volume, cooldowntrader
            //of all markets

            //Get Market data from critical Node
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

            if (dex == null) { return; }

            //We will grab open order information, along with chart data for the market
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
                        SqliteConnection mycon = new SqliteConnection("Data Source=\"" + App_Path + "/data/neblidex.db\";Version=3;");
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
                    NebliDexNetLog("Error retrieving chart data");
                }
            }

            //Now grab the open orders for the market
            int page = 0;
            int order_num = 1;
            try
            {
                lock (OpenOrderList[market])
                {
                    //Lock this list until all the data has been loaded in
                    OpenOrderList[market].Clear();

                    while (order_num > 0)
                    {
                        //Because orders can be very large, divide them per pages of information
                        string blockdata = "";
                        lock (dex.blockhandle)
                        {
                            dex.blockhandle.Reset();
                            SendCNServerAction(dex, 10, "" + market + ":" + page); //Get the open orders for the market
                            dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                            if (dex.blockdata == "") { return; }
                            blockdata = dex.blockdata;
                        }
                        JObject js = JObject.Parse(blockdata);
                        order_num = 0;
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
                                if (ord.order_stage > 0)
                                {
                                    ord.pendtime = UTCTime();
                                }
                                ord.cooldownend = Convert.ToUInt32(row["cool"].ToString());
								ord.price = Decimal.Parse(row["price"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
								ord.original_amount = Decimal.Parse(row["original"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
								ord.amount = Decimal.Parse(row["amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
								ord.minimum_amount = Decimal.Parse(row["min_amount"].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
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
            }
            catch (Exception)
            {
                NebliDexNetLog("Error retreiving market data");
            }

            //Then get the recent trade history
            try
            {
                string blockdata = "";
                lock (dex.blockhandle)
                {
                    dex.blockhandle.Reset();
                    SendCNServerAction(dex, 12, market + ":0"); //Just get the first page of recent data
                    dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                    if (dex.blockdata == "") { return; }
                    blockdata = dex.blockdata;
                }

                JObject js = JObject.Parse(blockdata);
                int recent_num = 0;
                if (js["cn.numpts"] != null)
                {
                    recent_num = Convert.ToInt32(js["cn.numpts"].ToString());
                }
                else
                {
                    return;
                }
                //Otherwise parse data
                lock (RecentTradeList[market])
                {
                    RecentTradeList[market].Clear();
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
            }
            catch (Exception)
            {
                NebliDexNetLog("Unable to acquire most recent trades for this market");
            }

            //Get the current candle stats, not stored in database
            //Now ask for the chart last prices for each timeframe
            //Will use this calculate their current calcluate
            for (int time = 0; time < 2; time++)
            {
                try
                {
                    int numpts = 0;
                    page = 0;
                    lock (ChartLastPrice)
                    {
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
                                return;
                            }
                            //Otherwise parse data                  
                            foreach (JToken row in js["cn.result"])
                            {
                                LastPriceObject la = new LastPriceObject();
                                la.market = Convert.ToInt32(row["market"].ToString());
                                la.price = Convert.ToDecimal(row["price"].ToString(), CultureInfo.InvariantCulture);
                                la.atime = Convert.ToInt32(row["atime"].ToString());

                                //First object is oldest
                                ChartLastPrice[time].Add(la);
                            }
                            page++;
                        } while (numpts > 0);
                    }
                }
                catch (Exception)
                {
                    NebliDexNetLog("Unable to get list of last chart prices");
                }
            }

            //Also, get the time sync value so we can sync with the Critical Node chart
            if (next_candle_time == 0)
            {
                //First time running program
                try
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    {
                        dex.blockhandle.Reset();
                        SendCNServerAction(dex, 14, "");
                        dex.blockhandle.WaitOne(5000); //This will wait 5 seconds for a response                                
                        if (dex.blockdata == "") { return; }
                        blockdata = dex.blockdata;
                    }

                    JObject js = JObject.Parse(blockdata);
                    next_candle_time = Convert.ToInt32(js["cn.candletime"].ToString());
                    if (next_candle_time < UTCTime())
                    {
                        next_candle_time = 0; //Somehow the times have become out of sync, set it to zero
                    }
                    ChartLastPrice15StartTime = next_candle_time - 60 * 15;
                    candle_15m_interval = Convert.ToInt32(js["cn.15minterval"].ToString());
                }
                catch (Exception e)
                {
                    NebliDexNetLog("Unable to sync clocks with Critical Node: " + e.ToString());
                }
            }

            //Now request the fee from the CN
            SendCNServerAction(dex, 52, ""); //Request for most current CN fee

        }

        public static string CNWaitResponse(NetworkStream nStream, TcpClient client, string json_encoded)
        {
            //Helper code to avoid code replication
            try
            {
                Byte[] data = System.Text.Encoding.ASCII.GetBytes(json_encoded);
                nStream.WriteTimeout = 30000; //30 second timeout for write

                uint data_length = (uint)json_encoded.Length;
                nStream.Write(Uint2Bytes(data_length), 0, 4); //Write the length of the bytes to be received
                nStream.Write(data, 0, data.Length); //Write to Server

                //Now receive this response
                Byte[] response_length = new Byte[4];
                int num_bytes = nStream.Read(response_length, 0, 4); //Read 4 bytes
                if (num_bytes == 0)
                {
                    throw new Exception("Failed to receive a response from the connected party");
                } //No response, possible timeout
                data_length = Bytes2Uint(response_length); //It will tell us our message length

                string response = "";
                Byte[] databuf = new Byte[1024]; //Our buffer
                int read_size = 0;
                while (data_length > 0)
                {
                    int read_amount = databuf.Length;
                    if (read_amount > data_length) { read_amount = (int)data_length; }
                    read_size = nStream.Read(databuf, 0, read_amount);
                    //Get the string
                    response = response + System.Text.Encoding.ASCII.GetString(databuf, 0, read_size);
                    if (data_length - read_size <= 0) { break; }
                    data_length -= Convert.ToUInt32(read_size);
                }
                return response;
            }
            catch (Exception e)
            {
                //Close all the stream data
                nStream.Close();
                client.Close();
                throw new Exception("No response from CN: " + e.ToString());
            }
        }

        public static void FindValidationNode(DexConnection con, string nonce, int who_validate)
        {
            //This function finds a validation node
            OpenOrder ord = null;
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(nonce) == true && MyOpenOrderList[i].queued_order == false)
                    {
                        ord = MyOpenOrderList[i];
                        if (ord.is_request == false)
                        {
                            //Maker order
                            if (who_validate == 0)
                            {
                                ord.validating = true;
                            }
                            else
                            {
                                ord.validating = false;
                            }
                        }
                        else
                        {
                            //Taker validating
                            if (who_validate == 1)
                            {
                                ord.validating = true;
                            }
                            else
                            {
                                ord.validating = false;
                            }
                        }
                        break;
                    }
                }
            }
            if (ord == null) { return; }

            //Now send a message to the dex to acquire a validation node for this order
            //The CN will then also forward this node info to the other node
            SendCNServerAction(con, 42, ord.order_nonce);
        }

        public static void CheckCNBlockHelperServerSync()
        {
            //Like Electrum server sync, verify the blockheights and compare
            //Ran every 15 minutes

            //Put this here in case Critical Node disconnects from BlockHelper due to connection issue
            //It tries to reconnect automatically
            if (critical_node == true)
            {
                //Try to connect the BlockHelper RPC
                ConnectBlockHelperNode();
                if (run_headless == true && using_blockhelper == true)
                {
                    Console.WriteLine("Connected to NebliDex BlockHelper");
                }
            }

            if (using_cnblockhelper == false) { return; }
            if (using_blockhelper == true) { return; }
            Dictionary<string, CriticalNode> CN_Node_List_Clone = new Dictionary<string, CriticalNode>(CN_Nodes_By_IP);
            if (CN_Node_List_Clone.Count < 1) { return; } //Only one critical node online
            NebliDexNetLog("Running CN Blockhelper server sync");

            //First get the blockheight of the current node         
            //Now get this one's blockheight
            JObject req = new JObject();
            req["cn.method"] = "blockhelper.getblockheight";
            req["cn.response"] = 0;
            string response = SendBlockHelperMessage(req, false);
            if (response.Length == 0)
            {
                //Failed to get a response in time
                NebliDexNetLog("Failed to receive a response from the CN blockhelper");
                return;
            }
            JObject result = JObject.Parse(response);
            if (result["cn.result"] == null)
            {
                //This means error
                NebliDexNetLog("Unable to obtain the blockchain height, error: " + result["cn.error"].ToString());
                return;
            }
            long blockheight = Convert.ToInt64(result["cn.result"].ToString()); //We will compare this number
            bool need_new_cn = false;

            for (int i = 0; i < 4; i++)
            {
                //Query up to 4 CN servers
                if (CN_Node_List_Clone.Count == 0) { break; }
                int pos = (int)Math.Round(GetRandomNumber(1, CN_Node_List_Clone.Count)) - 1;
                int index = 0;

                foreach (string ip in CN_Node_List_Clone.Keys)
                {
                    if (index == pos)
                    {
                        try
                        {
                            TcpClient client = new TcpClient();
                            NebliDexNetLog("Trying to connect to CN Blockhelper for blockchain sync at ip: " + ip);
                            IPAddress address = IPAddress.Parse(ip); //Localhost
                            if (ConnectSync(client, address, critical_node_port, 10))
                            {
                                NetworkStream nStream = client.GetStream();
                                nStream.ReadTimeout = 5000; //Wait 5 seconds before timing out on reads
                                nStream.WriteTimeout = 5000;

                                //Get Server version from the node and make sure it matches the client
                                JObject js = new JObject();
                                js["cn.method"] = "cn.getblockhelper_status";
                                js["cn.response"] = 0;
                                string json_encoded = JsonConvert.SerializeObject(js);

                                response = CNWaitResponse(nStream, client, json_encoded);
                                js = JObject.Parse(response);
                                if (js["cn.result"].ToString() == "Active")
                                {
                                    //This CN has its blockhelper active, get the blockheight
                                    NebliDexNetLog("Connected to CN Blockhelper for blockchain sync");
                                    js = new JObject();
                                    js["cn.method"] = "blockhelper.getblockheight";
                                    js["cn.response"] = 0;
                                    json_encoded = JsonConvert.SerializeObject(js);
                                    response = CNWaitResponse(nStream, client, json_encoded);
                                    js = JObject.Parse(response);
                                    if (js["cn.result"] != null)
                                    {
                                        long height = Convert.ToInt64(result["cn.result"].ToString());
                                        NebliDexNetLog("This blockhelper height: " + height);
                                        if (height - 5 > blockheight)
                                        {
                                            //The CN blockhelper height we are connected to is lower than the others
                                            need_new_cn = true;
                                            NebliDexNetLog("Disconnected our CN Blockhelper as our height is much lower");
                                        }
                                    }
                                }
                                nStream.Close();
                                client.Close();
                            }
                            else
                            {
                                throw new Exception("Failed to connect to Critical Node at IP for sync: " + ip);
                            }
                        }
                        catch (Exception e)
                        {
                            NebliDexNetLog("Failed to connect to the CN BlockHelper node for sync:" + e.ToString());
                        }
                        CN_Node_List_Clone.Remove(ip);
                        break;
                    }
                    index++;
                }
                if (need_new_cn == true) { break; }
            }

            if (need_new_cn == true)
            {
                lock (DexConnectionList)
                {
                    for (int i = 0; i < DexConnectionList.Count; i++)
                    {
                        if (DexConnectionList[i].open == true && DexConnectionList[i].contype == 5)
                        {
                            DexConnectionList[i].open = false;
                        }
                    }
                }
                using_cnblockhelper = false;
                ConnectCNBlockHelper();
            }
        }

		public static void CheckElectrumServerSync()
        {

            //This function will make sure that the connected electrum server is synchronized with its network
            //It is ran every 15 minutes and if not synced the connection will be dropped
            for (int etype = 1; etype < total_cointypes; etype++)
            {
                if (etype == 1)
                {
                    NebliDexNetLog("Checking Bitcoin blockchain sync");
                }
                else if (etype == 2)
                {
                    NebliDexNetLog("Checking Litecoin blockchain sync");
                }
                else if (etype == 3)
                {
                    NebliDexNetLog("Checking Groestlcoin blockchain sync");
                }
                else if (etype == 4)
                {
                    NebliDexNetLog("Checking Bitcoin Cash blockchain sync");
                }
                else if (etype == 5)
                {
                    NebliDexNetLog("Checking Monacoin blockchain sync");
                }
                else if (etype == 6)
                {
                    continue; //Ethereum doesn't use DexConnection
                }
                DexConnection dex = null;
                lock (DexConnectionList)
                {
                    for (int i2 = 0; i2 < DexConnectionList.Count; i2++)
                    {
                        if (DexConnectionList[i2].contype == 1 && DexConnectionList[i2].open == true && DexConnectionList[i2].blockchain_type == etype)
                        {
                            dex = DexConnectionList[i2];
                            break;
                        }
                    }
                }

                if (dex == null) { continue; } //Move along to next dex

                try
                {
                    string blockdata = "";
                    lock (dex.blockhandle)
                    { //Prevents other threads from doing blocking calls to connection
                        dex.blockhandle.Reset();
                        SendElectrumAction(dex, 10, "");
                        dex.blockhandle.WaitOne(10000); //This will wait 10 seconds for a response
                        if (dex.blockdata == "")
                        {
                            NebliDexNetLog("No electrum response");
                            //Disconnect from server so it doesn't happen again
                            dex.open = false;
                            continue;
                        } //No response yet
                        blockdata = dex.blockdata;
                    }
                    int blockheight = Convert.ToInt32(blockdata); //This is the current blockheight
                    if (etype == 1)
                    {
                        NebliDexNetLog("Our Bitcoin blockheight: " + blockheight);
                    }
                    else if (etype == 2)
                    {
                        NebliDexNetLog("Our Litecoin blockheight: " + blockheight);
                    }
                    else if (etype == 3)
                    {
                        NebliDexNetLog("Our Groestlcoin blockheight: " + blockheight);
                    }
                    else if (etype == 4)
                    {
                        NebliDexNetLog("Our Bitcoin Cash blockheight: " + blockheight);
                    }
                    else if (etype == 5)
                    {
                        NebliDexNetLog("Our Monacoin blockheight: " + blockheight);
                    }

                    //Now we will query 4 electrum servers of same type for their blockheights
                    //And if ours is lower than theirs, we disconnect ours

                    bool only_server = true;

                    for (int i = 0; i < 4; i++)
                    {
                        //Connect to electrum servers
                        string[] electrum_info = new string[2];
                        bool ok = SelectRandomElectrum(etype - 1, electrum_info);
						if (ok == false)
                        {
                            //Couldn't find a server
                            break;
                        }
                        if (electrum_info[0].Equals(dex.ip_address[0]) == true) { continue; }
                        try
                        {
                            only_server = false; //We are not the only server running
                            IPAddress address = IPAddress.Parse(electrum_info[0]);
                            int port = Convert.ToInt32(electrum_info[1]);
                            TcpClient client = new TcpClient();
                            //This will wait 5 seconds before moving on (bad connection)
                            NebliDexNetLog("Trying to connect to peer for sync: " + electrum_info[0]);
                            if (ConnectSync(client, address, port, 5))
                            {
                                NebliDexNetLog("Connected To Electrum peer for sync: " + electrum_info[0]);
                                NetworkStream nStream = client.GetStream();
                                SslStream secure_nStream = new SslStream(nStream, true, new RemoteCertificateValidationCallback(CheckSSLCertificate), null);
                                secure_nStream.ReadTimeout = 5000;
                                secure_nStream.WriteTimeout = 5000;
                                try
                                {
                                    secure_nStream.AuthenticateAsClient(electrum_info[0]);
                                }
                                catch (Exception e)
                                {
                                    secure_nStream.Close();
                                    nStream.Close();
                                    client.Close();
                                    throw new Exception("Invalid certificate from server, error: " + e.ToString());
                                }

                                //Transmit server version to make sure this is accepted or not
                                JObject js = new JObject();
                                js["id"] = 0;
                                js["method"] = "server.version";
                                js["params"] = new JArray("NebliDex", "1.4"); //ElectrumX is now the official repository

                                string json_encoded = JsonConvert.SerializeObject(js);

                                Byte[] data = System.Text.Encoding.ASCII.GetBytes(json_encoded + "\r\n");
                                secure_nStream.Write(data, 0, data.Length); //Write to Server
                                                                            //And then receive the response
                                Byte[] databuf = new Byte[1024]; //Our buffer
                                int packet_size = 1;
                                string response = "";

                                while (packet_size > 0)
                                {
                                    packet_size = NSReadLine(secure_nStream, databuf, 0, databuf.Length); //Read into buffer
                                                                                                          //Get the string
                                    response = response + System.Text.Encoding.ASCII.GetString(databuf, 0, packet_size);
                                    if (packet_size < databuf.Length)
                                    {
                                        //Message is finished
                                        break;
                                    }
                                }

                                NebliDexNetLog("Electrum server response: " + response);

                                js = JObject.Parse(response);
                                if (js["result"] == null)
                                {
                                    //This server version not accepted
                                    //Try another
                                    secure_nStream.Close();
                                    nStream.Close();
                                    client.Close();
                                    throw new System.InvalidOperationException("Electrum server version too low");
                                }

                                //Now try to obtain the current block height
                                js = new JObject();
                                js["id"] = 1;
                                js["method"] = "blockchain.headers.subscribe";
                                js["params"] = new JArray();

                                json_encoded = JsonConvert.SerializeObject(js);

                                data = System.Text.Encoding.ASCII.GetBytes(json_encoded + "\r\n");
                                secure_nStream.Write(data, 0, data.Length); //Write to Server
                                                                            //And then receive the response
                                packet_size = 1;
                                response = "";
                                while (packet_size > 0)
                                {
                                    packet_size = NSReadLine(secure_nStream, databuf, 0, data.Length); //Read into buffer
                                                                                                       //Get the string
                                    response = response + System.Text.Encoding.ASCII.GetString(databuf, 0, packet_size);
                                    if (packet_size < data.Length)
                                    {
                                        //Message is finished
                                        break;
                                    }
                                }

                                js = JObject.Parse(response);
                                if (js["result"] == null)
                                {
                                    //No blockheight available
                                    //Try another
                                    secure_nStream.Close();
                                    nStream.Close();
                                    client.Close();
                                    throw new System.InvalidOperationException("Unable to acquire current block height: " + response);
                                }

                                int e_blockheight = Convert.ToInt32(js["result"]["height"].ToString()); //What we want
                                NebliDexNetLog("Peer blockheight: " + e_blockheight);

                                secure_nStream.Close();
                                nStream.Close();
                                client.Close();

                                if (e_blockheight - 2 > blockheight)
                                {
                                    //Our connected server is out of sync, disconnect it
                                    NebliDexNetLog("Disconnecting electrum server, out of sync");
                                    dex.open = false;
                                    break;
                                }
                            }
                            else
                            {
                                client.Close();
                                throw new System.InvalidOperationException("Server timeout");
                            }
                        }
                        catch (Exception e)
                        {
                            //Something went wrong with lookup, just move to next electrum
                            NebliDexNetLog("Unable to connect to: " + electrum_info[0] + ", error: " + e.ToString());
                        }
                    }

                    if (only_server == true)
                    {
                        //Find new electrum servers as we are the only server on the list left
                        NebliDexNetLog("Failed to find more than one server for blockchain " + etype);
                        if (testnet_mode == false)
                        {
                            File.Delete(App_Path + "/data/electrum_peers.dat"); //Remove this to force a relook on mainnet
                            FindElectrumServers();
                        }
                    }


                }
                catch (Exception e)
                {
                    NebliDexNetLog("Failed to sync electrum server, error: " + e.ToString());
                }
            }
        }

		public static bool ConnectSync(TcpClient client, IPAddress ad, int port, int timeout_sec)
        {
            //This method will create a task and block until the time is up
            //It will also create an exception handler for unhandled tasks
            Task t = client.ConnectAsync(ad, port);
            t.ContinueWith(e => { NebliDexNetLog("Task exception: " + e.Exception.ToString()); }, TaskContinuationOptions.OnlyOnFaulted);
            if (t.Wait(TimeSpan.FromSeconds(timeout_sec)))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static async void StartHeadlessCN(object state)
        {
            //This code attempts to open the client as a critical node
            await Task.Run(() => ToggleCNStatus(null));
            if (critical_node == false)
            {
                Console.WriteLine("Failed to connect as Critical Node. Will close program.");
                NebliDexNetLog("Failed to connect as Critical Node.");
                Headless_Application_Close();
            }
            else
            {
                Console.WriteLine("Successfully connected as Critical Node!");
                Console.WriteLine("Running as Critical Node...");
            }
        }

        //Atomic Transactions
		public static bool TakerReceiveValidatorInfo(DexConnection con, JObject js)
        {
            //This function will start the validation process with the validation node
            //In this case, the taker will receive the information and create the initiator smart contract
            string validator_ip = js["cn.validator_ip"].ToString();
            string order_nonce = js["cn.order_nonce"].ToString();
            string validator_pubkey = js["cn.validator_pubkey"].ToString();

            NebliDexNetLog("Taker received initial validation information: " + order_nonce);

            OpenOrder myord = null;
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(order_nonce) == true && MyOpenOrderList[i].is_request == true && MyOpenOrderList[i].queued_order == false)
                    {
                        myord = MyOpenOrderList[i];
                        if (myord.validating == true && js["cn.initiating_cn_ip"].ToString() != con.ip_address[0])
                        {
                            //Someone else chose this validator, not cool if my turn
                            return false;
                        }

                        if (myord.validating == true && validator_ip.Equals(con.ip_address[0]) == true)
                        {
                            //The validating IP is equal to the connection IP and I'm validating, should not normally happen
                            //Unless CN_Count is very low (minus 3 in case validator chosen is maker, taker or connected CN
							if (MarketCNNodeCount(myord.market) - 3 > 0) { return false; }
                        }

                        if (myord.order_stage >= 2)
                        {
                            //This means it has already received validation information
                            NebliDexNetLog("Already received validation information");
                            return false;
                        }

                        //Set this my open order to sending transaction. This prevents taker from sending balance more than once
                        myord.order_stage = 2;

                    }
                }
            }

            if (myord == null) { return false; } //Order not linked to me

            //Contact the validation node and get signatures, then verify balance
            try
            {
                IPAddress address = IPAddress.Parse(validator_ip);
                TcpClient client = new TcpClient();
                //This will wait 30 seconds before moving on (bad connection)
                if (ConnectSync(client, address, critical_node_port, 30))
                {
                    NebliDexNetLog("Taker Connected To Validating Node at: " + validator_ip);
                    //Connect to the critical node and get order request info
                    client.ReceiveTimeout = 30000; //30 seconds
                    NetworkStream nStream = client.GetStream(); //Get the stream

                    JObject vob = new JObject();
                    vob["cn.method"] = "cn.validatorgetinfo";
                    vob["cn.response"] = 0;
                    vob["cn.order_nonce"] = order_nonce;
                    vob["cn.validator_pubkey"] = validator_pubkey; //Validator uses this to confirm if maker has right node
                    vob["cn.validator_ip"] = validator_ip;
                    vob["cn.is_maker"] = 0; //Taker is requesting info
                    vob["cn.getinfo_only"] = 0; //Tell CN will are connecting as validator node
                    vob["trade.rsa_pubkey"] = my_rsa_pubkey; //Send our pubkey

                    string json_encoded = JsonConvert.SerializeObject(vob);
                    string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                    //Convert it to a JSON
                    //The other critical node will return a response before relaying to other nodes
                    //Thus freeing up this node to do stuff
                    JObject result = JObject.Parse(response);
                    NebliDexNetLog("Get Info Response: " + response);
                    if (result["trade.recipient_add"] == null)
                    {
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    string destination_add = result["trade.recipient_add"].ToString(); //Get the Maker's receive address

                    //There is a good result to respond to
                    string validator_sig = result["cn.validator_sig"].ToString();
                    string sessionkey_encrypt = result["cn.sessionkey"].ToString(); //This will be encrypted with RSA pubkey
                    string sessionkey_sig = result["cn.sessionkey_sig"].ToString();
                    string sessionkey = DecryptRSAText(sessionkey_encrypt, my_rsa_privkey); //Get the one-time AES key by decrypting with our RSA privatekey

                    //Verify IP is related to validation Node
                    PubKey pub = new PubKey(validator_pubkey);
                    bool check = pub.VerifyMessage(validator_ip, validator_sig);
                    if (check == false)
                    {
                        //Message was altered
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    //Verify encrypted Sessionkey is related to validation node
                    check = pub.VerifyMessage(sessionkey_encrypt, sessionkey_sig);
                    if (check == false)
                    {
                        //Message was altered
                        NebliDexNetLog("Validator encryption key is corrupted");
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    //Generate a NEBL address from the public key
                    string validator_add = "";
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
                        validator_add = pub.GetAddress(my_net).ToString();
                    }

                    decimal balance = GetBlockchainAddressBalance(3, validator_add, false);
                    if (balance < cn_ndex_minimum)
                    {
                        //Not enough balance to be a CN
                        NebliDexNetLog("Validator doesn't have enough balance to complete trade");
                        nStream.Close();
                        client.Close();
                        return false;
                    }
                    NebliDexNetLog("Validator NDEX balance is: " + balance);

                    Decimal vn_fee = Convert.ToDecimal(result["trade.ndex_fee"].ToString(), CultureInfo.InvariantCulture);

                    //Make sure VN_Fee is not too much higher than current ndex fee
                    if (vn_fee > ndex_fee * 1.1m || vn_fee < 0)
                    {
                        NebliDexNetLog("NDEX Fee was higher than expected or below zero");
                        //It can at most be 10% greater than current fee
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    int redeemscript_wallet = 0; //The cointype of the redeem script account (will be the same as the send wallet)
                    Decimal sendamount = 0;
                    Decimal receiveamount = 0; //The expected amount to receive from the maker in the other cointype
                    Decimal subtractfee = 0;
                    int makerwallet = 0; //Cointype the maker is sending on
                    if (myord.type == 0)
                    {
                        //I'm buying
                        receiveamount = myord.amount; //The amount we are requesting
                        if (MarketList[myord.market].trade_wallet == 3)
                        {
                            //NDEX so no fee (but will be deducted from receive balance)
                            receiveamount -= vn_fee / 2m; //Buying NDEX, so no fee but reduce balance expected
                            vn_fee = 0;
                        }
                        else
                        {
                            vn_fee = vn_fee / 2m;
                        }
                        sendamount = Decimal.Round(myord.amount * myord.price, 8); //We are sending the base amount
                        redeemscript_wallet = MarketList[myord.market].base_wallet;
                        makerwallet = MarketList[myord.market].trade_wallet;
                    }
                    else
                    {
                        //I'm selling
                        if (MarketList[myord.market].trade_wallet == 3)
                        {
                            //NDEX so I pay all the fee
                            //But I subtract what I send the maker by half fee (CNs already know this)
                            subtractfee = vn_fee / 2m;
                        }
                        else
                        {
                            vn_fee = vn_fee / 2m;
                        }
                        sendamount = myord.amount - subtractfee; //We are sending the trade amount minus any subtract fees
                        receiveamount = Decimal.Round(myord.amount * myord.price, 8);
                        redeemscript_wallet = MarketList[myord.market].trade_wallet;
                        makerwallet = MarketList[myord.market].base_wallet;
                    }

                    //Now get the address of the wallet we are sending from
                    //We will refund to this wallet if contract times out
                    string my_send_add = GetWalletAddress(redeemscript_wallet);

                    //Determine if this is an Ethereum based atomic swap
                    bool sending_eth = false;
					bool sending_erc20 = false;
                    if (GetWalletBlockchainType(redeemscript_wallet) == 6)
                    {
                        sending_eth = true;
                        if (Wallet.CoinERC20(redeemscript_wallet) == true)
                        {
                            sending_erc20 = true; //We are sending ERC20 token
                        }
                    }

                    //Now create the Smart Contract
                    //Initiator contract last for 3 hours after creation
                    string contract_secret = CreateAtomicSwapSecret(); //Fixed length secret that must be kept hidden, used to redeem maker contract
                    string contract_secret_hash = ConvertByteArrayToHexString(NBitcoin.Crypto.Hashes.SHA256(ConvertHexStringToByteArray(contract_secret))); //SHAHash the secret to protect it
                                                                                                                                                            //Now create the redeem script used by the secret
                                                                                                                                                            //We must add the block inclusion time of the maker's contract in case maker wants to refund
                    long maker_inclusion_time = GetBlockInclusionTime(makerwallet, 0); //This will return the extra time to add because of maker inclusion time
                    long contract_unlock_time = UTCTime() + max_transaction_wait + maker_inclusion_time; //3 hours + inclusion time
                    long refund_contract_time = GetBlockInclusionTime(redeemscript_wallet, contract_unlock_time); //Some blockchains prevent nLockTimes greater than median of last 11 blocks

                    string contract_address;
                    string atomic_contract_string = "Not Present";
                    if (sending_eth == false)
                    {
                        Script atomic_contract_script = CreateAtomicSwapScript(destination_add, my_send_add, contract_secret_hash, contract_unlock_time);
                        atomic_contract_string = atomic_contract_script.ToString();
                        contract_address = CreateAtomicSwapAddress(redeemscript_wallet, atomic_contract_script); //This is the Scripthash address
                    }
                    else
                    {
                        contract_address = ETH_ATOMICSWAP_ADDRESS;
						if (sending_erc20 == true)
                        {
                            contract_address = ERC20_ATOMICSWAP_ADDRESS; //Sending to the ERC20 contract
                        }
                    }
                    //We need to send the secret hash and the unlock time to the maker to proceed, along with payment and fee transaction to critical node

                    //Participant contract lasts for 1.5 hours after creation

                    //Bitcoin public/private keypair not suitable for encryption, must use RSA encryption for keys
                    //Then symmetric encryption (AES) for actual transaction

                    bool nebliobased = false;
                    bool ntp1_wallet = IsWalletNTP1(redeemscript_wallet);
                    if (redeemscript_wallet == 0 || ntp1_wallet == true)
                    {
                        //The redeem script add is of nebliotype
                        nebliobased = true;
                    }

                    //If neblio based, the transaction includes fee to validation node
                    //The validator node won't propagate the transaction unless the fee is paid
                    Transaction send_tx = null;
                    Transaction sendfee_tx = null;
                    Nethereum.Signer.TransactionChainId eth_tx = null;
                    string my_txhash = "";
                    if (nebliobased == false)
                    {
                        //Bitcoin based coins will use normal P2PKH via electrum
                        //We will send extra coin to cover the eventual transfer out of the scripthash address
                        if (sending_eth == false)
                        {
                            send_tx = CreateSignedP2PKHTx(redeemscript_wallet, sendamount, contract_address, false, true, "", 0, 0, true);
                        }
                        else
                        {
							if (sending_erc20 == false)
                            {
                                string open_data = GenerateEthereumAtomicSwapOpenData(contract_secret_hash, destination_add, contract_unlock_time);
                                eth_tx = CreateSignedEthereumTransaction(redeemscript_wallet, contract_address, sendamount, true, 1, open_data);
                            }
                            else
                            {
                                //Sending ERC20 to contract, we already have this amount approved via approval transaction
                                BigInteger int_sendamount = ConvertToERC20Int(sendamount, GetWalletERC20TokenDecimals(redeemscript_wallet));
                                string token_contract = GetWalletERC20TokenContract(redeemscript_wallet);
                                string open_data = GenerateERC20AtomicSwapOpenData(contract_secret_hash, int_sendamount, token_contract, destination_add, contract_unlock_time);
                                eth_tx = CreateSignedEthereumTransaction(redeemscript_wallet, contract_address, sendamount, true, 1, open_data);
                            }
                        }
                        if (vn_fee > 0)
                        {
                            //Create an NTP1 transaction for the fee as well to validator
                            //Just a straight payment to the validation node
                            sendfee_tx = CreateSignedP2PKHTx(3, vn_fee, validator_add, false, true);

                            if (sendfee_tx == null)
                            {
                                NebliDexNetLog("Unable to create fee transaction");
                                nStream.Close();
                                client.Close();
                                return false;
                            }
                            UpdateWalletStatus(3, 2); //The wallet becomes unavailable
                        }
                    }
                    else
                    {
                        //Create a singular transaction that pays the validator as well as transfer neblio/tokens
                        Decimal extra_neb = 0; //Giving the other person extra neb so they can trade more
                        int maker_blockchain_type = GetWalletBlockchainType(makerwallet);
                        if (redeemscript_wallet == 3 && maker_blockchain_type != 0 && myord.type == 1)
                        {
                            //We are selling NDEX for coin other than NEBL, give them some NEBL too so they can trade with it
                            extra_neb = blockchain_fee[0] * 12;
                        }
                        send_tx = CreateSignedP2PKHTx(redeemscript_wallet, sendamount, contract_address, false, true, validator_add, vn_fee, extra_neb, true);
                    }

                    if (send_tx == null && eth_tx == null)
                    {
                        //Something went wrong
                        NebliDexNetLog("Unable to create normal transaction");
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    //The order amount is reduced at recent trade
                    UpdateWalletStatus(redeemscript_wallet, 2); //Put wallet into wait mode

                    vob = new JObject();
                    vob["cn.method"] = "cn.validator_takerinfo";
                    vob["cn.response"] = 0;
                    vob["cn.order_nonce"] = order_nonce;
                    vob["cn.initiating_cn_ip"] = js["cn.initiating_cn_ip"]; //Forward the initating IP to Taker
                    if (sending_eth == false)
                    {
                        vob["trade.validator_mytx"] = AESEncrypt(send_tx.ToHex(), sessionkey); //Transaction encrypted so only validator can post it                    
                        my_txhash = send_tx.GetHash().ToString();
                    }
                    else
                    {
                        vob["trade.validator_mytx"] = AESEncrypt(eth_tx.Signed_Hex, sessionkey);
                        my_txhash = eth_tx.HashID;
                    }
                    if (sendfee_tx != null)
                    {
                        vob["trade.validator_feetx"] = AESEncrypt(sendfee_tx.ToHex(), sessionkey);
                    }
                    else
                    {
                        vob["trade.validator_feetx"] = "";
                    }
                    vob["trade.ndex_add"] = GetWalletAddress(3); //Used for the validator to check fees
                    vob["trade.unlock_time"] = contract_unlock_time;
                    vob["trade.contract_add"] = contract_address;
                    vob["trade.taker_send_add"] = my_send_add;
                    vob["trade.secret_hash"] = contract_secret_hash; //These are all the things needed to recreate the contract

                    //Add transaction to send to the database
                    int reqtime = Convert.ToInt32(result["trade.reqtime"].ToString());
                    //Type 0 is taker to maker indirect transaction
                    AddMyTxToDatabase(my_txhash, my_send_add, contract_address, sendamount, redeemscript_wallet, 0, reqtime, order_nonce);

                    SetMyTransactionData("to_add_redeemscript", atomic_contract_string, reqtime, order_nonce);
                    SetMyTransactionData("counterparty_cointype", makerwallet, reqtime, order_nonce);
                    SetMyTransactionData("atomic_unlock_time", contract_unlock_time, reqtime, order_nonce);
                    SetMyTransactionData("atomic_refund_time", refund_contract_time, reqtime, order_nonce);
                    SetMyTransactionData("receive_amount", receiveamount.ToString(CultureInfo.InvariantCulture), reqtime, order_nonce);
                    SetMyTransactionData("atomic_secret_hash", contract_secret_hash, reqtime, order_nonce);
                    SetMyTransactionData("atomic_secret", contract_secret, reqtime, order_nonce); //Only the Taker has the Atomic Secret
                    SetMyTransactionData("validating_nodes", validator_ip, reqtime, order_nonce); //Set the validator IP in case connection drops

                    //We will obtain the maker's contract redeem script later and contract address later

                    if (sendfee_tx != null)
                    {
                        //Sending from NDEX wallet as separate transaction
                        AddMyTxToDatabase(sendfee_tx.GetHash().ToString(), GetWalletAddress(3), validator_add, vn_fee, 3, 2, reqtime, order_nonce);
                    }

                    //And add a recent trade information into the database as pending
                    AddMyRecentTrade(myord.market, myord.type, myord.price, myord.amount, my_txhash, 1);

                    //Now create a short term Dex connection
                    DexConnection dex = new DexConnection();
                    dex.ip_address[0] = validator_ip;
                    dex.ip_address[1] = critical_node_port.ToString();
                    dex.outgoing = true;
                    dex.open = true; //The connection is open
                    dex.contype = 4; //Validation connection
                    dex.tn_connection_nonce = order_nonce; //Classify this connection
                    dex.client = client;
                    dex.stream = nStream;
                    lock (DexConnectionList)
                    {
                        DexConnectionList.Add(dex);
                    }

                    //Setup the callback that runs when data is received into connection
                    nStream.BeginRead(dex.buf, 0, 1, DexConnCallback, dex);
                    SendCNServerAction(dex, 46, JsonConvert.SerializeObject(vob)); //This may take some time so we need a persistent connection
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Validating Node Error: " + validator_ip + " error: " + e.ToString());
                NebliDexNetLog("Taker failed to validate trade");
                return false;
            }

            NebliDexNetLog("Taker finished validating: " + order_nonce);
            return true;
        }

		public static bool MakerReceiveValidatorInfo(DexConnection con, JObject js)
        {
            //This function will start the validation process with the validation node
            string validator_ip = js["cn.validator_ip"].ToString();
            string order_nonce = js["cn.order_nonce"].ToString();
            string validator_pubkey = js["cn.validator_pubkey"].ToString();

            NebliDexNetLog("Maker received validation information: " + order_nonce);

            OpenOrder myord = null;
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(order_nonce) == true && MyOpenOrderList[i].is_request == false && MyOpenOrderList[i].queued_order == false)
                    {
                        myord = MyOpenOrderList[i];
                        if (myord.validating == true && js["cn.initiating_cn_ip"].ToString() != con.ip_address[0])
                        {
                            //Someone else chose this validator, not cool if my turn
                            return false;
                        }

                        if (myord.validating == true && validator_ip.Equals(con.ip_address[0]) == true)
                        {
                            //The validating IP is equal to the connection IP and I'm validating, should not normally happen
                            //Unless CN_Count is very low (minus 3 in case validator chosen is maker, taker or connected CN
							if (MarketCNNodeCount(myord.market) - 3 > 0) { return false; }
                        }

                        if (myord.order_stage >= 3)
                        {
                            //This means it has already received validation information
                            NebliDexNetLog("Already received validation information");
                            return false;
                        }

                        //Set this my open order to sending transaction, prevent it from being closed by user, just temporarily
                        myord.order_stage = 3;

                    }
                }
            }

            if (myord == null) { return false; } //Order not linked to me

            //Contact the validation node and get signatures, then verify balance
            try
            {
                IPAddress address = IPAddress.Parse(validator_ip);
                TcpClient client = new TcpClient();
                //This will wait 30 seconds before moving on (bad connection)
                if (ConnectSync(client, address, critical_node_port, 30))
                {
                    NebliDexNetLog("Maker Connected To Validating Node at: " + validator_ip);
                    //Connect to the critical node and get order request info
                    client.ReceiveTimeout = 30000; //30 seconds
                    NetworkStream nStream = client.GetStream(); //Get the stream

                    DexConnection dex = new DexConnection();
                    dex.ip_address[0] = validator_ip;
                    dex.ip_address[1] = critical_node_port.ToString();
                    dex.outgoing = true;
                    dex.open = true; //The connection is open
                    dex.contype = 4; //Validation connection
                    dex.tn_connection_nonce = order_nonce; //Classify this connection
                    dex.client = client;
                    dex.stream = nStream;

                    JObject vob = new JObject();
                    vob["cn.method"] = "cn.validatorgetinfo";
                    vob["cn.response"] = 0;
                    vob["cn.order_nonce"] = order_nonce;
                    vob["cn.validator_pubkey"] = validator_pubkey; //Validator uses this to confirm if maker has right node
                    vob["cn.validator_ip"] = validator_ip;
                    vob["cn.is_maker"] = 1; //Maker is requesting info
                    vob["cn.getinfo_only"] = 0; //Tell CN will are connecting as validator node
                    vob["trade.rsa_pubkey"] = my_rsa_pubkey; //Send our pubkey

                    string json_encoded = JsonConvert.SerializeObject(vob);
                    string response = CNWaitResponse(nStream, client, json_encoded); //This code will wait/block for a response

                    //Convert it to a JSON
                    //The other critical node will return a response before relaying to other nodes
                    //Thus freeing up this node to do stuff
                    JObject result = JObject.Parse(response);
                    NebliDexNetLog("Get Info Response: " + response);
                    if (result["trade.recipient_add"] == null)
                    {
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    string destination_add = result["trade.recipient_add"].ToString(); //Get the taker's receive address
                    int reqtime = Convert.ToInt32(result["trade.reqtime"].ToString());

                    //There is a good result to respond to
                    string validator_sig = result["cn.validator_sig"].ToString();
                    string sessionkey_encrypt = result["cn.sessionkey"].ToString(); //This will be encrypted with RSA pubkey
                    string sessionkey_sig = result["cn.sessionkey_sig"].ToString();
                    string sessionkey = DecryptRSAText(sessionkey_encrypt, my_rsa_privkey); //Get the one-time AES key by decrypting with our RSA privatekey

                    //Verify IP is related to validation Node
                    PubKey pub = new PubKey(validator_pubkey);
                    bool check = pub.VerifyMessage(validator_ip, validator_sig);
                    if (check == false)
                    {
                        //Message was altered
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    //Verify Sessionkey is related to validation node
                    check = pub.VerifyMessage(sessionkey_encrypt, sessionkey_sig);
                    if (check == false)
                    {
                        //Message was altered
                        NebliDexNetLog("Validator encryption key is corrupted");
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    //Generate a NEBL address from the public key
                    string validator_add = "";
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
                        validator_add = pub.GetAddress(my_net).ToString();
                    }

                    decimal balance = GetBlockchainAddressBalance(3, validator_add, false);
                    if (balance < cn_ndex_minimum)
                    {
                        //Not enough balance to be a CN
                        NebliDexNetLog("Validator doesn't have enough balance to complete trade");
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        return false;
                    }
                    NebliDexNetLog("Validator NDEX balance is: " + balance);

                    Decimal vn_fee = Convert.ToDecimal(result["trade.ndex_fee"].ToString(), CultureInfo.InvariantCulture);

                    //Make sure VN_Fee is not too much higher than current ndex fee
                    if (vn_fee > ndex_fee * 1.1m || vn_fee < 0)
                    {
                        NebliDexNetLog("NDEX Fee was higher than expected or below zero");
                        //It can at most be 10% greater than current fee
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    Decimal trade_amount = Convert.ToDecimal(result["trade.amount"].ToString(), CultureInfo.InvariantCulture);

                    //Check trade_amount
                    Decimal min_amount = myord.minimum_amount;
                    if (min_amount > myord.amount) { min_amount = myord.amount; }
                    if (trade_amount > myord.amount || trade_amount < min_amount)
                    { //The amount requested cannot be greater than openorder amount
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        NebliDexNetLog("Amount requested is invalid");
                        return false;
                    }

                    int maker_receivewallet = 0;
                    int taker_receivewallet = 0;
                    Decimal sendamount = 0; //Amount maker is sending
                    Decimal receiveamount = 0; //Amount maker is expected to receive
                    Decimal subtractfee = 0;
                    if (myord.type == 0)
                    {
                        //I'm buying, taker is selling
                        maker_receivewallet = MarketList[myord.market].trade_wallet;
                        taker_receivewallet = MarketList[myord.market].base_wallet;
                        receiveamount = trade_amount;
                        sendamount = Decimal.Round(trade_amount * myord.price, 8);
                        if (MarketList[myord.market].trade_wallet == 3)
                        {
                            //NDEX so no fee (but will be deducted from receive balance)
                            receiveamount -= vn_fee / 2m; //Buying NDEX, so no fee but reduce balance expected
                            vn_fee = 0;
                        }
                        else
                        {
                            vn_fee = vn_fee / 2m;
                        }
                    }
                    else
                    {
                        //I'm selling
                        maker_receivewallet = MarketList[myord.market].base_wallet;
                        taker_receivewallet = MarketList[myord.market].trade_wallet;
                        if (MarketList[myord.market].trade_wallet == 3)
                        {
                            //NDEX so I pay all the fee
                            //But I subtract what I send the taker by half fee (CNs already know this)
                            subtractfee = vn_fee / 2m;
                        }
                        else
                        {
                            vn_fee = vn_fee / 2m;
                        }
                        sendamount = trade_amount - subtractfee; //We are sending the trade amount minus any subtract fees
                        receiveamount = Decimal.Round(trade_amount * myord.price, 8);
                    }

                    //Recreate taker contract
                    //Make sure critical taker contract info is there
                    if (js["trade.taker_contract_add"] == null)
                    {
                        //There is no taker contract address present
                        NebliDexNetLog("There is no taker contract address present");
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    long taker_unlock_time = Convert.ToInt64(js["trade.taker_unlock_time"].ToString());
                    string prelim_taker_contract_add = js["trade.taker_contract_add"].ToString();
                    string taker_send_add = js["trade.taker_send_add"].ToString(); //The address that the taker coins is coming from
                    string secret_hash = js["trade.secret_hash"].ToString();

                    //Now validate the contract
                    //Calculate our inclusion time and subtract from taker unlocktime
                    long maker_inclusion_time = GetBlockInclusionTime(taker_receivewallet, 0); //This will return the extra time to remove because of my inclusion time
                    long adjusted_ctime = taker_unlock_time - max_transaction_wait - maker_inclusion_time; //Subtract 3 hours and maker contract inclusion time
                    if (Math.Abs(UTCTime() - adjusted_ctime) > 60 * 3)
                    { //Contract time difference greater than 3 minutes, not allowed
                        NebliDexNetLog("Contract time difference is too great");
                        TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                        nStream.Close();
                        client.Close();
                        return false;
                    }

                    Script taker_contract = null;
                    string taker_contract_address = "";
                    if (GetWalletBlockchainType(maker_receivewallet) != 6)
                    {
                        //Taker is sending non-Eth to maker
                        taker_contract = CreateAtomicSwapScript(GetWalletAddress(maker_receivewallet), taker_send_add, secret_hash, taker_unlock_time);
                        taker_contract_address = CreateAtomicSwapAddress(maker_receivewallet, taker_contract);

                        if (taker_contract_address.Equals(prelim_taker_contract_add) == false)
                        {
                            //Taker contract doesn't match the recreated one, not right
                            NebliDexNetLog("Generated taker contract address doesn't match given");
                            TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                            nStream.Close();
                            client.Close();
                            return false;
                        }
                    }
                    else
                    {
                        //The taker is sending Ethereum to the smart contract address
                        taker_contract_address = ETH_ATOMICSWAP_ADDRESS;
						if (Wallet.CoinERC20(maker_receivewallet) == true)
                        {
                            //We are receiving ERC20, make sure the taker contract matches
                            taker_contract_address = ERC20_ATOMICSWAP_ADDRESS;
                        }
                        if (taker_contract_address.Equals(prelim_taker_contract_add) == false)
                        {
                            //Make sure taker has the same contract address as we do
                            NebliDexNetLog("Taker ethereum contract doesn't match mine.");
                            TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                            nStream.Close();
                            client.Close();
                            return false;
                        }
                    }

                    //Now we generate our own maker contract
                    //This expires in 1.5 hours after taker contract creation
                    string my_send_add = GetWalletAddress(taker_receivewallet);

                    //Determine if we are sending an enthereum smart contract
                    bool sending_eth = false;
					bool sending_erc20 = false;
                    if(GetWalletBlockchainType(taker_receivewallet) == 6){
                        sending_eth = true;
                        if(Wallet.CoinERC20(taker_receivewallet) == true){
                            sending_erc20 = true;
                        }
                    }

                    long contract_unlock_time = adjusted_ctime + max_transaction_wait / 2; //1.5 hours the contract will expire 
                    long refund_contract_time = GetBlockInclusionTime(taker_receivewallet, contract_unlock_time);

                    string atomic_contract_string = "Not Present";
                    string contract_address;
                    if (sending_eth == false)
                    {
                        Script atomic_contract_script = CreateAtomicSwapScript(destination_add, my_send_add, secret_hash, contract_unlock_time);
                        atomic_contract_string = atomic_contract_script.ToString();
                        contract_address = CreateAtomicSwapAddress(taker_receivewallet, atomic_contract_script);
                    }
                    else
                    {
                        contract_address = ETH_ATOMICSWAP_ADDRESS;
						if (sending_erc20 == true)
                        {
                            contract_address = ERC20_ATOMICSWAP_ADDRESS;
                        }
                    }

                    //Bitcoin public/private keypair not suitable for encryption, must use RSA encryption for keys
                    //Then symmetric encryption (AES) for actual transaction

                    //At this point, we will only send a neblio based fee transaction to the validating node so that it lets taker know we are ready
                    //After taker puts expected balance (including blockchain fees) into contract will maker broadcast transaction into maker contract
                    Transaction sendfee_tx = null;
                    if (vn_fee > 0)
                    {
                        //Create an NTP1 transaction for the fee as well to validator
                        //Just a straight payment to the validation node
                        sendfee_tx = CreateSignedP2PKHTx(3, vn_fee, validator_add, false, true);
                        if (sendfee_tx == null)
                        {
                            NebliDexNetLog("Unable to create fee transaction");
                            TraderRejectTrade(dex, order_nonce, reqtime, true); //Send message to cancel trade
                            nStream.Close();
                            client.Close();
                            return false;
                        }
                        UpdateWalletStatus(3, 2); //The wallet becomes unavailable
                    }

                    vob = new JObject();
                    vob["cn.method"] = "cn.validator_makerconfirm";
                    vob["cn.response"] = 0;
                    vob["cn.order_nonce"] = order_nonce;
                    vob["cn.reqtime"] = result["trade.reqtime"];
                    if (sendfee_tx != null)
                    {
                        vob["trade.validator_feetx"] = AESEncrypt(sendfee_tx.ToHex(), sessionkey);
                    }
                    else
                    {
                        vob["trade.validator_feetx"] = ""; //No fee
                    }
                    vob["trade.validator_mytx"] = ""; //Not sending our transaction
                    vob["trade.ndex_add"] = GetWalletAddress(3); //Used for the validator to check fees
                    vob["trade.contract_add"] = contract_address; //Taker will eventually try to recreate this contract
                    vob["trade.maker_send_add"] = my_send_add;

                    //Add transaction to send to the database                   
                    //Type 5 is maker to taker indirect transaction, waiting for payment
                    //We do not have a linked transaction yet to this contract (no money has been sent yet)
                    if (sending_eth == false)
                    {
                        AddMyTxToDatabase("", my_send_add, contract_address, sendamount, taker_receivewallet, 5, reqtime, order_nonce);
                    }
                    else
                    {
                        //We store the taker's receive address of the ethereum contract directly, this is different than taker
                        //As we need to create a contract based on it
                        AddMyTxToDatabase("", my_send_add, destination_add, sendamount, taker_receivewallet, 5, reqtime, order_nonce);
                    }
                    SetMyTransactionData("to_add_redeemscript", atomic_contract_string, reqtime, order_nonce);
                    SetMyTransactionData("counterparty_cointype", maker_receivewallet, reqtime, order_nonce);
                    SetMyTransactionData("atomic_unlock_time", contract_unlock_time, reqtime, order_nonce);
                    SetMyTransactionData("atomic_refund_time", refund_contract_time, reqtime, order_nonce);
                    SetMyTransactionData("receive_amount", receiveamount.ToString(CultureInfo.InvariantCulture), reqtime, order_nonce);
                    SetMyTransactionData("atomic_secret_hash", secret_hash, reqtime, order_nonce);
                    SetMyTransactionData("custodial_redeemscript_add", taker_contract_address, reqtime, order_nonce);
                    //Redeem script is used to redeem from taker contract once secret is known
                    if (taker_contract != null)
                    {
                        SetMyTransactionData("custodial_redeemscript", taker_contract.ToString(), reqtime, order_nonce);
                    }
                    SetMyTransactionData("validating_nodes", validator_ip, reqtime, order_nonce); //Set the validator IP in case connection drops

                    if (sendfee_tx != null)
                    {
                        //Sending from NDEX wallet as separate transaction
                        AddMyTxToDatabase(sendfee_tx.GetHash().ToString(), GetWalletAddress(3), validator_add, vn_fee, 3, 2, reqtime, order_nonce);
                    }

                    //And add a recent trade information into the database as pending even though we haven't sent to contract yet
                    AddMyRecentTrade(myord.market, myord.type, myord.price, trade_amount, secret_hash, 1); //We will use the secret hash as pointer to recent trade

                    //Now add the validator to our connection list
                    lock (DexConnectionList)
                    {
                        DexConnectionList.Add(dex);
                    }

                    //Setup the callback that runs when data is received into connection
                    nStream.BeginRead(dex.buf, 0, 1, DexConnCallback, dex);
                    SendCNServerAction(dex, 46, JsonConvert.SerializeObject(vob)); //This may take some time so we need a persistent connection

                    //We now wait for the taker to fill the contract address before broadcasting to it

                    //Now reduce maker order amount
                    //Then finally decrease our open order amount and remove it if its below or equal to 0
                    //This makes the assumption that the taker's transaction will go through
                    myord.amount -= trade_amount;
                    myord.amount = Math.Round(myord.amount, 8); //Make sure we don't get weird values
                    if (myord.amount <= 0)
                    {
                        RemoveSavedOrder(myord);
                        MyOpenOrderList.Remove(myord);
						Application.Invoke(delegate
                        {
                            if (main_window_loaded == true)
                            {
                                main_window.Open_Order_List_Public.NodeStore.RemoveNode(myord);
                            }
                        });
                    }
                    else
                    {
                        UpdateSavedOrder(myord);
                    }

                    //Update the view
					Application.Invoke(delegate
                    {
                        if (main_window_loaded == true)
                        {
                            main_window.Open_Order_List_Public.QueueDraw(); //Force redraw of the list
                        }
                    });
                }
            }
            catch (Exception e)
            {
                NebliDexNetLog("Validating Node Error: " + validator_ip + " error: " + e.ToString());
                NebliDexNetLog("Maker failed to validate trade");
                return false;
            }

            NebliDexNetLog("Maker finished validating: " + order_nonce);
            return true;
        }

        public static void TraderRejectTrade(DexConnection con, string order_nonce, int reqtime, bool is_maker)
        {
            //The Taker or the Maker is rejecting the trade
            //Send the message to the validation node
            JObject txinfo = new JObject();
            txinfo["cn.method"] = "cn.validator_trade_reject";
            txinfo["cn.response"] = 0;
            txinfo["cn.order_nonce"] = order_nonce;
            txinfo["cn.utctime"] = reqtime;
            if (is_maker == false)
            {
                txinfo["cn.is_maker"] = 0;
                txinfo["cn.result"] = "Taker rejects this trade, please cancel it.";
            }
            else
            {
                txinfo["cn.is_maker"] = 1;
                txinfo["cn.result"] = "Maker rejects this trade, please cancel it.";
            }
            SendCNServerAction(con, 46, JsonConvert.SerializeObject(txinfo));
        }

		public static void TakerConfirmTrade(DexConnection con, JObject js)
        {
            //Final step before validating node leaves the rest of the process up to the nodes themselves
            //Taker will confirm with validator that information is correct or not
            string order_nonce = js["cn.order_nonce"].ToString();
            int reqtime = Convert.ToInt32(js["cn.reqtime"].ToString());

            bool trade_ok = true;

            NebliDexNetLog("Taker received secondary validation information: " + order_nonce);

            OpenOrder myord = null;
            lock (MyOpenOrderList)
            {
                for (int i = 0; i < MyOpenOrderList.Count; i++)
                {
                    if (MyOpenOrderList[i].order_nonce.Equals(order_nonce) == true && MyOpenOrderList[i].is_request == true && MyOpenOrderList[i].queued_order == false)
                    {
                        myord = MyOpenOrderList[i];

                        if (myord.order_stage >= 3)
                        {
                            //This means it has already received maker's validation information
                            NebliDexNetLog("Already received maker's validation information");
                            return;
                        }

                        //Set this my open order to waiting for maker's balance
                        myord.order_stage = 3;

                    }
                }
            }

            if (myord == null) { return; } //Order not linked to me

            string prelim_maker_contract_add = js["cn.maker_contract_add"].ToString();
            string maker_send_add = js["trade.maker_send_add"].ToString();

            //We will now recreate the maker contract to us
            if (GetMyTransactionData("atomic_unlock_time", reqtime, order_nonce).Length == 0)
            {
                //For some reason, not available
                return;
            }

            long taker_unlock_time = Convert.ToInt64(GetMyTransactionData("atomic_unlock_time", reqtime, order_nonce)); //First get taker unlock_time
            string secret_hash = GetMyTransactionData("atomic_secret_hash", reqtime, order_nonce);
            int taker_receivewallet = Convert.ToInt32(GetMyTransactionData("counterparty_cointype", reqtime, order_nonce));
            long maker_inclusion_time = GetBlockInclusionTime(taker_receivewallet, 0);
            long maker_unlock_time = taker_unlock_time - maker_inclusion_time - max_transaction_wait / 2; //Then calculate maker unlock time            
            string my_receive_add = GetWalletAddress(taker_receivewallet);

            if (GetWalletBlockchainType(taker_receivewallet) != 6)
            {
                Script maker_contract_script = CreateAtomicSwapScript(my_receive_add, maker_send_add, secret_hash, maker_unlock_time);
                string maker_contract_add = CreateAtomicSwapAddress(taker_receivewallet, maker_contract_script);

                if (maker_contract_add.Equals(prelim_maker_contract_add) == false)
                {
                    NebliDexNetLog("Maker contract doesn't match expected");
                    trade_ok = false;
                }

                //Store the redeem script and the address
                SetMyTransactionData("custodial_redeemscript_add", maker_contract_add, reqtime, order_nonce);
                SetMyTransactionData("custodial_redeemscript", maker_contract_script.ToString(), reqtime, order_nonce);
            }
            else
            {
                //We are receiving Ethereum from the smart contract, will wait for a balance
				string expected_contract = ETH_ATOMICSWAP_ADDRESS;
                if (Wallet.CoinERC20(taker_receivewallet) == true)
                {
                    //We are receiving ERC20
                    expected_contract = ERC20_ATOMICSWAP_ADDRESS;
                }
                if (prelim_maker_contract_add.Equals(expected_contract) == false)
                {
                    NebliDexNetLog("Maker ethereum contract address doesn't match expected");
                    trade_ok = false;
                }
            }

            JObject vjs = new JObject();
            vjs["cn.method"] = "cn.validator_takerconfirm";
            vjs["cn.response"] = 1;
            vjs["cn.order_nonce"] = order_nonce;
            vjs["cn.reqtime"] = reqtime;

            if (trade_ok == true)
            {
                vjs["cn.result"] = "Taker Accepts";
                //Validator will broadcast transaction shortly after

                //Now remove the open order from the list for the taker and monitor the maker contract
                //Remove the taker's open order from its window
                lock (MyOpenOrderList)
                {
                    MyOpenOrderList.Remove(myord);
                }
                //And update the view
                if (main_window_loaded == true)
                {
					Application.Invoke(delegate
                    {
						main_window.Open_Order_List_Public.NodeStore.RemoveNode(myord); //Remove object for node list
                    });
                }
            }
            else
            {
                //Alert validator that I rejected the trade
                vjs["cn.result"] = "Taker Rejects";
            }
            SendCNServerAction(con, 46, JsonConvert.SerializeObject(vjs));

            NebliDexNetLog("Taker finished processing secondary validation information: " + order_nonce);
        }

		public static int MarketCNNodeCount(int market)
        {
            //Returns the number of CNs that support this market
            int count = 0;
            lock (CN_Nodes_By_IP)
            {
                foreach (CriticalNode cn in CN_Nodes_By_IP.Values)
                {
                    if (market < cn.total_markets)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

		public static bool CheckErrorMessage(string msg)
        {
            //This method checks error messages to make sure they fit pre-defined list
            //Used to prevent malicious strings from being transmitted to client (ie. Electrum hack)
            switch (msg)
            {
                case "Request Failed: Your trade request has been rejected":
                    return true;
                case "Order Request Denied: Invalid Order Data":
                    return true;
                case "Order Request Denied: Amount must be positive":
                    return true;
                case "Order Request Denied: Token must be indivisible.":
                    return true;
                case "Order Request Denied: There is already an open request on this order right now. Try again later.":
                    return true;
                case "Order Request Denied: This is your order":
                    return true;
                case "Order Request Denied: Order already in trade":
                    return true;
                case "Order Request Denied: Amount less than minimum":
                    return true;
                case "Order Request Denied: Amount more than order amount":
                    return true;
                case "Order Request Denied: Amount too small based on the current CN fee":
                    return true;
                case "Order Request Denied: This order request is too small":
                    return true;
                case "Order Request Denied: You do not have enough balance to match this order":
                    return true;
                case "Order Request Denied: Original Order Doesn't Exist":
                    return true;
                case "Order Denied: Invalid Order Data":
                    return true;
                case "Order Denied: Amount must be positive":
                    return true;
                case "Order Denied: Minimum amount must be positive":
                    return true;
                case "Order Denied: Minimum order amount cannot be greater than order amount":
                    return true;
                case "Order Denied: Price must be positive":
                    return true;
                case "Order Denied: Price is greater than 10 000 000":
                    return true;
                case "Order Denied: Total order too small":
                    return true;
                case "Order Denied: Amount too small based on the current CN fee":
                    return true;
                case "Order Denied: Please resubmit order":
                    return true;
                case "Order Denied: Exceeded max amount of open orders at one time.":
                    return true;
                case "Please upgrade your version of NebliDex by visiting NebliDex.xyz (Not .com)":
                    return true;
                case "Your version is above my Server version. Cannot connect.":
                    return true;
                case "CN Rejected Or Already Accepted":
                    return true;
                case "CN Rejected: Client Version Too Old":
                    return true;
                case "CN Rejected: Client Found on CN Blacklist":
                    return true;
                case "CN Rejected: Client has same IP as active CN":
                    return true;
                case "CN Rejected: Signature Verification Failed":
                    return true;
                case "CN Rejected: NDEX balance below CN minimum":
                    return true;
                case "CN Rejected: Client time is not synced with active CN time":
                    return true;
                case "CN Rejected: Unable to connect to your Client. Check your firewall.":
                    return true;
                case "CN Rejected: Client IP must match sending IP":
                    return true;
                case "Order Request Denied: You do not have enough balance to match this order in the Ethereum contract":
                    return true;
				case "Order Request Denied: You have not authorized enough allowance to complete this trade":
                    return true;
				case "Your total markets are greater than my Server markets. Cannot connect.":
                    return true;
                default:
                    return false;
            }
        }
		
	}
	
}