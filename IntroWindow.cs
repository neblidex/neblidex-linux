using System;
using System.Threading;
using Gtk;

namespace NebliDex_Linux
{
    public partial class IntroWindow : Gtk.Window
    {
		public Gtk.Label Intro_Status;
		public ManualResetEvent waiting = null;
		   
        public IntroWindow() :
                base(Gtk.WindowType.Toplevel)
        {
            this.Build();
			Banner.File = App.App_Path+"/logo.png";
			Intro_Status = Intro_Status_Private;
			this.DeleteEvent += Intro_Closed;

			App_Initiate(); //Start the program
        }

		private void App_Initiate()
        {
            //Create task to perform activity
            if (App.main_window != null) { return; }

            App.Start(this);

        }

		public void Intro_Closed(object sender, DeleteEventArgs args)
		{
			if(App.main_window == null){
				App.http_open_network = false;
				Application.Quit();
				args.RetVal = false;
				Environment.Exit(0);
			}
			if(waiting != null){
				//Allow the calling thread to continue
				waiting.Set();
			}
		}
    }
}
