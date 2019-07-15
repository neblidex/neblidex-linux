# NebliDex - An atomic swap powered decentralized exchange
NebliDex is a full service decentralized exchange that users can use to trade cryptocurrencies such as Bitcoin, Litecoin, Ethereum, Neblio, Monacoin, Groestlcoin, Bitcoin Cash and Neblio based assets (NTP1 tokens) without using a centralized service or match maker. Bitcoin in NebliDex represents actual Bitcoin. Ethereum in NebliDex represents actual Ethereum. There are no gateways to convert Bitcoin to any other representative tokens. The trades are performed using atomic swap functionality as defined by the Decred specification with some modifications and the Ethereum atomic swap contract can be found here: https://etherscan.io/address/0xcfd9c086635cee0357729da68810a747b6bc674a

The matchmaking is performed by Critical Nodes on the network which are volunteers that have at least 39,000 NDEX tokens. Matchmakers/Validators get rewarded by receiving NDEX tokens for their service. Anyone can become a validator as long as they meet the requirements specified in readme_first document.

## Bug Reports
If a bug or vulnerability is found, please report it immediately via our bug report form: https://www.neblidex.xyz/bugreport/

## Getting Started
First read readme_first.html before creating any trade. NebliDex is very intuitive.

## Building NebliDex
NebliDex is built in C# using managed code from the .NET Library on Windows and Mono Framework on Mac and Linux.
NebliDex uses Newtonsoft.JSON library (JSON.NET) and SQLite Library Version 3
### Mac
* Download Visual Studio for Mac
* Install Mono Framework (if not already included)
* Open Solution
* Build and Run in Terminal

If you want to create a bundle that is not dependent on Mono, see mkbundle command from Mono Framework

### Linux
Depending on the exact distribution of Linux you are running the steps can vary.
* Install Mono Develop from here: https://www.monodevelop.com/download/#fndtn-download-lin
* Open Solution
* Build and Run in Terminal

If you want to create a bundle that is not dependent on Mono, see mkbundle command from Mono Framework

### Windows
* Make sure at least .NET Framework 4.5 is installed on your system
* Find your favorite C# code editor (Visual Studio, SharpDevelop,MonoDevelop)
* Open Solution
* Build and Run in Terminal

## Release Notes
### https://www.neblidex.xyz/downloads/#release_notes
