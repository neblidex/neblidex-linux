# NebliDex - An atomic swap powered decentralized exchange
NebliDex is a full service decentralized exchange that users can use to trade cryptocurrencies such as Bitcoin, Litecoin, Neblio and Neblio based assets (NTP1 tokens) without using a centralized service or match maker. Bitcoin in NebliDex represent actual Bitcoin. There are no gateways to convert Bitcoin to any other representative tokens. The trades are performed using atomic swap functionality as defined by the Decred specification with some modifications. The matchmaking is performed by Critical Nodes on the network which are volunteers that have at least 39,000 NDEX tokens. Matchmakers/Validators get rewarded by receiving NDEX tokens for their service. Anyone can become a validator as long as they meet the requirements specified in readme_first document.

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
* Run code: `sudo apt-get install monodevelop`
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
