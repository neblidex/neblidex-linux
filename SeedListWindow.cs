using System;
using Gtk;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

namespace NebliDex_Linux
{
    public partial class SeedListWindow : Gtk.Window
    {
		public ManualResetEvent waiting = null;

        public SeedListWindow(string dns) :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
            //Old height was 165
			Gtk.Label connect_label = (Gtk.Label)Connect_Button.Children[0];
            connect_label.Markup = "<span font='14'>Connect</span>";
			if (App.DNS_SEED_TYPE == 0)
            {
                this.DNS_Field.Text = dns;
            }
            else
            {
                this.IP_Field.Text = dns;
            }

            if (App.main_window_loaded == true)
            {
				connect_label.Markup = "<span font='14'>Update</span>";;
            }

			Reset_Button.Clicked += Reset_DNS;
			this.DeleteEvent += SeedWindow_Closed;
			Connect_Button.Clicked += Find_Nodes;
        }

		private void Reset_DNS(object sender, EventArgs e)
        {
            this.DNS_Field.Text = App.Default_DNS_SEED;
        }

		private void SeedWindow_Closed(object sender, DeleteEventArgs args)
        {
            if (waiting != null)
            {
                //Allow the calling thread to continue
                waiting.Set();
            }
        }

		private async void Find_Nodes(object sender, EventArgs e)
        {
            if (IP_Field.Text.Trim().Length > 0)
            {
                App.DNS_SEED_TYPE = 1; //IP Address
                App.DNS_SEED = IP_Field.Text.Trim();
            }
            else
            {
                App.DNS_SEED = DNS_Field.Text.Trim();
                App.DNS_SEED_TYPE = 0; //Http address
            }
            Connect_Button.Sensitive = false; //Disable the button
			Gtk.Label connect_label = (Gtk.Label)Connect_Button.Children[0];
			connect_label.Markup = "<span font='14'>Connecting...</span>";

            if (App.main_window_loaded == true)
            { //Only delete if we are trying to update the CN list
                File.Delete(App.App_Path + "/data/cn_list.dat");
            }

            await Task.Run(() => App.FindCNServers(true)); //Get the Nodes

			Application.Invoke(delegate
            {
				SeedWindow_Closed(null, null); //Free any locks
				this.Destroy();
            });
        }
    }
}
