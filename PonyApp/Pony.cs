﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Timers;
using WpfAnimatedGif;

namespace PonyApp {

	public class Pony {

		public static List<PonyDirection> ValidDirections { get; private set; }

		///////////////////////////////////////////////////////////////////////
		// instance properties ////////////////////////////////////////////////

		/// <summary>
		/// the name of the pony. also matches the folder name her images are
		/// stored in.
		/// </summary>
		public String Name { get; set; }

		/// <summary>
		/// the yoffset is for fudging the pony's position to make her appear
		/// to be standing on the taskbar. each pony is different as some of
		/// them have animations that require more whitespace below their
		/// hooves than others.
		/// </summary>
		private int YOffset { get; set; }

		/// <summary>
		/// an number 0 to 100 that represents how much energy this pony has.
		/// when a pony hits 0 energy then she sleeps.
		/// </summary>
		private double Energy { get; set; }

		/// <summary>
		/// if she should sleep depending on the time of day (and night).
		/// </summary>
		public bool SleepTOD { get; set; }

		/// <summary>
		/// lists of all the actions this pony said she is able to perform
		/// in her this.pony file.
		/// </summary>
		public List<PonyAction> AvailableActions { get; private set; }
		public List<PonyAction> AvailableActiveActions { get; private set; }
		public List<PonyAction> AvailablePassiveActions { get; private set; }

		/// <summary>
		/// ponyality structure that modifies her decision making.
		/// </summary>
		private Ponyality Ponyality { get; set; }

		/// <summary>
		/// the mode/mood/stance that the pony is currently in such as free
		/// range, being still, or being clingy.
		/// </summary>
		public PonyMode Mode { get; set; }

		/// <summary>
		/// the action the pony is currently performing. things like trotting,
		/// standing, whatever.
		/// </summary>
		public PonyAction Action { get; set; }

		/// <summary>
		/// the direction the pony is facing. e.g. to the left... or the right.
		/// </summary>
		public PonyDirection Direction { get; set; }

		/// <summary>
		/// this is the physical window on screen that the pony will manipulate
		/// to perform her actions.
		/// </summary>
		public PonyWindow Window { get; set; }

		/// <summary>
		/// stores all the images this pony needs in a list so that we don't
		/// have to ping disk every x seconds.
		/// </summary>
		public List<PonyImage> Image { get; set; }

		/// <summary>
		/// a random number generator seeded special for this pony.
		/// </summary>
		public Random RNG { get; set; }

		/// <summary>
		/// this timer is for moving the window around at an interval. however
		/// as it is if too many ponies are moving the ui thread starts to
		/// jitter, so i need to figure out how to put these in a separate
		/// thread and still have permission to modify the pony objects.
		/// </summary>
		private DispatcherTimer WindowTimer;

		/// <summary>
		/// this timer powers the choice engine that allows the pony to make her
		/// own choices when she wants.
		/// </summary>
		private DispatcherTimer ChoiceTimer;

		/// <summary>
		/// this timer is for clingy mode where, every interval she will check
		/// where the mouse cursor is and follow it.
		/// </summary>
		private DispatcherTimer ClingTimer;

		/// <summary>
		/// create a new pony from the configuration. it will initalize allthe
		/// required properties including building the lists of actions (active
		/// and passive) that this pony instance is capable of doing.
		/// </summary>
		public Pony(PonyConfig config) {
			this.RNG = new Random();

			this.Name = config.Name;
			this.YOffset = config.YOffset;
			this.Ponyality = config.Ponyality;
			this.Energy = 400;
			this.SleepTOD = true;

			// prepare available action lists. copy on purpose.
			this.AvailableActions = new List<PonyAction>(config.Actions);
			this.AvailableActiveActions = new List<PonyAction>();
			this.AvailablePassiveActions = new List<PonyAction>();

			// for each available action, classify them as active or passive
			// for our decision making later.
			this.AvailableActions.ForEach(delegate(PonyAction action){
				if(Enum.IsDefined(typeof(PonyActionActive), (int)action))
					this.AvailableActiveActions.Add(action);

				if(Enum.IsDefined(typeof(PonyActionPassive), (int)action))
					this.AvailablePassiveActions.Add(action);
			});

			Trace.WriteLine(String.Format(
				"== {0} can perform {1} Active and {2} Passive actions",
				this.Name,
				this.AvailableActiveActions.Count,
				this.AvailablePassiveActions.Count
			));

			Trace.WriteLine(String.Format(
				"== {0} Ponyality:\r\n   {1}",
				this.Name,
				this.Ponyality.ToString()
			));

			// prepare action properties.
			this.Mode = PonyMode.Free;
			this.Action = PonyAction.None;
			this.Direction = PonyDirection.None;

			// various timers.
			this.ChoiceTimer = null;
			this.WindowTimer = null;
			this.ClingTimer = null;

			// physical stuff.
			this.Window = new PonyWindow(this);
			this.Image = new List<PonyImage>();

			// preload action images.
			this.LoadAllImages();

			Trace.WriteLine(String.Format("// {0} says hello",this.Name));

			// if a pony can teleport lets let them poof in.
			if(this.CanDo(PonyAction.Teleport)) {
				this.LoadImage(PonyAction.Teleport,PonyDirection.Right);
				this.TeleportStage();
			}

			// else have them walk in from the edge.
			else {
				this.LoadImage(PonyAction.Trot, PonyDirection.Right);
				this.Window.Left = 0 - (this.Window.Width + 10);
				this.TellWhatDo(PonyAction.Trot,PonyDirection.Right);
			}

			this.Window.Show();
		}

		public void Free() {

			Trace.WriteLine(String.Format("// {0} waves goodbye", this.Name));

			// stop and trash all the timers.
			if(this.WindowTimer != null) this.WindowTimer.Stop();
			if(this.ChoiceTimer != null) this.ChoiceTimer.Stop();
			if(this.ClingTimer != null) this.ClingTimer.Stop();
			this.WindowTimer = this.ChoiceTimer = this.ClingTimer = null;

			// release all the images we cached.
			this.Image.ForEach(delegate(PonyImage img){
				img.Free();
				img = null;
			});
			this.Image.Clear();
			this.Image = null;

			// release the window.
			this.Window.Hide();
			this.Window.Free();
			this.Window = null;

			// release the action lists.
			this.AvailableActions.Clear();
			this.AvailableActiveActions.Clear();
			this.AvailablePassiveActions.Clear();
			this.AvailableActions = this.AvailableActiveActions = this.AvailablePassiveActions = null;
			this.Ponyality = null;
		}

		///////////////////////////////////////////////////////////////////////
		// decision methods ///////////////////////////////////////////////////

		/// <summary>
		/// reset the choice timer. this means reseed it with a new interval
		/// value with a random number so that her choices feel random. we use
		/// different rng values depending the types of actions she is doing
		/// to hopefully create more natural feeling behaviour.
		/// </summary>
		public void ResetChoiceTimer() {

			if(this.ChoiceTimer.IsEnabled)
				this.ChoiceTimer.Stop();

			// ponies in motion tend to stay in motion, while ponies at
			// rest tend to stay at rest.

			if(this.IsActive()) {
				Trace.WriteLine(String.Format("// {0} feels energized",this.Name));

				// personal quirks should be given a little longer to run than generic
				// actions.
				if(this.Action == PonyAction.Action1 || this.Action == PonyAction.Action2)
				this.ChoiceTimer.Interval = TimeSpan.FromSeconds(this.RNG.Next(6, 10));

				// default action time.
				else
				this.ChoiceTimer.Interval = TimeSpan.FromSeconds(this.RNG.Next(4, 8));

			} else {
				Trace.WriteLine(String.Format("// {0} feels lazy", this.Name));

				// sleeping should be given a long time.
				if(this.Action == PonyAction.Sleep)
				this.ChoiceTimer.Interval = TimeSpan.FromSeconds(this.RNG.Next(15,30)*60);

				else
				this.ChoiceTimer.Interval = TimeSpan.FromSeconds(this.RNG.Next(12, 20));
			}

			this.ChoiceTimer.Start();

		}

		/// <summary>
		/// this version of choose what do responds to the choice engine timer
		/// deciding it is time to do something. it will tell the pony to
		/// choose what to do and reseed the choice timer.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		public void ChooseWhatDo(object sender, EventArgs e) {
			this.ChooseWhatDo();
		}

		/// <summary>
		/// allow the pony to choose what she wants to do next, based on the
		/// current mode she is in.
		/// </summary>
		public void ChooseWhatDo() {
			PonyState choice;

			// if it is late (or early) the pony should sleep like anypony
			// would. the 15-30min choice timer will still engage so at most
			// she will roll over from side to side while sleeping.
			if(this.SleepTOD) {
				if(DateTime.Now.Hour >= 23 || DateTime.Now.Hour <= 8) {
					this.EnergyReset();
					this.TellWhatDo(PonyAction.Sleep,this.ChooseDirection());
					return;
				}
			}

			// if we asked the pony to be still, restrict her options.
			if(this.Mode == PonyMode.Still) {
				Trace.WriteLine(String.Format(
					"// {0} knows you would like her to be still",
					this.Name
				));
				choice = this.DecideFromPassiveActions();
			}

			// let the pony do whatever she wants if we are allowing here to
			// frolic free.
			else if(this.Mode == PonyMode.Free) {
				choice = this.DecideByPonyality();
			}

			// else fallback to being free if unknown mode is unknown.
			else {
				choice = this.DecideByPonyality();
			}

			this.TellWhatDo(choice.Action, choice.Direction);
		}

		/// <summary>
		/// tell the pony exactly what to do (even if she is telling herself).
		/// if she is devoted then she will not allow herself to be distracted
		/// until this action is done.
		/// </summary>
		/// <param name="action"></param>
		/// <param name="direction"></param>
		/// <param name="devoted"></param>
		public void TellWhatDo(PonyAction action, PonyDirection direction, bool devoted) {

			if(devoted) {
				// she is devoted to doing this and will stop making decisions
				// for herself.
				this.PauseChoiceEngine();
			} else {
				// after she does this she is free to make other choices on
				// her own.
				this.ResetChoiceTimer();
			}

			this.TellWhatDo(action, direction);
		}

		/// <summary>
		/// tell the pony exactly what to do (even if she is telling herself).
		/// </summary>
		/// <param name="action"></param>
		/// <param name="direction"></param>
		public void TellWhatDo(PonyAction action, PonyDirection direction) {
			bool able = true;

			// if an invalid action was specified, then no.
			if(direction == PonyDirection.None || action == PonyAction.None) able = false;

			// if this is an action she is not configured to be able to do then
			// of course the answer is no.
			if(!this.CanDo(action)) able = false;

			if(!able) {
				Trace.WriteLine(String.Format(
					"!! {0} cannot {1} {2}",
					this.Name,
					action.ToString(),
					direction.ToString()
				));
				return;
			}

			Trace.WriteLine(String.Format(
				">> {0} will {1} to the {2}",
				this.Name,
				action.ToString(),
				direction.ToString()
			));

			// if this is the first action our pony has done, then we also need to
			// spool the decision engine up.
			if(this.ChoiceTimer == null) {
				this.ChoiceTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, this.Window.Dispatcher);
				this.ChoiceTimer.Tick += ChooseWhatDo;
				this.ResetChoiceTimer();
			}

			// no need to muck with the image and window if we are doing more of the
			// same yeh? also check choicetimer as a means of "is this the first
			// action ever" to make sure the default gets loaded.
			if(action != this.Action || direction != this.Direction) {
				this.Action = action;
				this.Direction = direction;
				this.StartAction();

				// reset the choice timer to a new interval based on the action
				// that just happened, but only if it was still enabled.
				if(this.ChoiceTimer.IsEnabled)
					this.ResetChoiceTimer();

			}

			// spend the energy associated with this action.
			if(this.Mode != PonyMode.Clingy)
			this.EnergySpend();
		}

		/// <summary>
		/// can the pony do the action requested?
		/// </summary>
		/// <param name="action"></param>
		public bool CanDo(PonyAction action) {

			// allow anypony to teleport in if they can teleport out.
			if(action == PonyAction.Teleport2)
			return this.AvailableActions.Contains(PonyAction.Teleport);

			else
			return this.AvailableActions.Contains(action);
		}

		/// <summary>
		/// choose which direction to go.
		/// </summary>
		private PonyDirection ChooseDirection() {

			switch(this.RNG.Next(1,3)) {
				case 1: return PonyDirection.Left;
				case 2: return PonyDirection.Right;
				default: return PonyDirection.None;
			}

		}

		/// <summary>
		/// have the pony to choose from any of her available actions.
		/// </summary>
		private PonyAction ChooseAction() {
			return this.AvailableActions[this.RNG.Next(0,this.AvailableActions.Count)];
		}

		/// <summary>
		/// have the pony to choose her action but only from her available
		/// active actions. things that do things.
		/// </summary>
		private PonyAction ChooseActiveAction() {
			return this.AvailableActiveActions[this.RNG.Next(0, this.AvailableActiveActions.Count)];
		}

		/// <summary>
		/// have the pony to choose her action but only from her available
		/// passive actions. things that mostly are still and not annoying.
		/// </summary>
		private PonyAction ChoosePassiveAction() {
			return this.AvailablePassiveActions[this.RNG.Next(0, this.AvailablePassiveActions.Count)];
		}

		/// <summary>
		/// tell the pony to choose her next action and direction but only
		/// from her available passive actions.
		/// </summary>
		private PonyState DecideFromPassiveActions() {

			var choice = new PonyState {
				Action = ChoosePassiveAction(),
				Direction = ChooseDirection()
			};

			return choice;
		}


		/// <summary>
		/// allow the pony's personality to choose her next action and
		/// direction from any of her available actions. she is a free pony
		/// and you should never tell her otherwise.
		/// 
		/// eventually this will be tweakable via the this.pony file for each
		/// pony to make them all unique to their own respective personalities.
		/// </summary>
		private PonyState DecideByPonyality() {
			var choice = new PonyState();
			bool undecided = false;
			bool lazy = false;

			// if the pony is too tired then she should go to sleep. if she can
			// sleep, that is.
			if(this.Energy <= 0) {
				if(this.CanDo(PonyAction.Sleep)) {
					choice.Direction = this.Direction;

					// if she is doing something active, stop first instead of
					// just falling asleep in mid sprint.
					if(this.IsActive()) {
						choice.Action = this.DecideFromPassiveActions().Action;
					} else {
						choice.Action = PonyAction.Sleep;
						this.Energy = 400;
					}

					return choice;
				} else {
					// else just reset it.
					this.Energy = 400;
				}
			}

			do {

				choice.Action = this.ChooseAction();
				choice.Direction = this.ChooseDirection();
				undecided = false;

				///////////////////////////////////////////////////////////////
				// directional choices ////////////////////////////////////////

				// direction choices are up for grabs every time a pony thinks
				// she had decided what to do. if she elects to be indecisive
				// about her actions later then she will rethink her direction
				// as well.

				// does the pony want to change directions? pony does not like
				// to change direction too often while performing actions.
				if(choice.Direction != this.Direction && !this.RandomChance(this.Ponyality.Spazziness)) {

					// if pony is doing something active and will continue to
					// do so then there is a higher chance she will not change
					// directions.
					if(this.IsActive() && Pony.IsActionActive(choice.Action))
					choice.Direction = this.Direction;

					// if pony is doing something active and she suddenly stops
					// then this too will have a greater chance of not changing
					// directions.
					if(this.IsActive() && !Pony.IsActionActive(choice.Action))
					choice.Direction = this.Direction;

				}

				///////////////////////////////////////////////////////////////
				// action choices /////////////////////////////////////////////

				// apply personality quirks to the pony's decision making
				// system to allow for subdued but unique behaviour.

				// don't ever do these.
				if(choice.Action == PonyAction.Sleep || choice.Action == PonyAction.Teleport2) {
					undecided = true;
					continue;
				}

				// if she has decided she is being lazy...
				if(lazy) {
					return this.DecideFromPassiveActions();
				}

				// pony generally like be lazy. if she is doing somethng lazy
				// there is a greater chance she will not want to do something
				// active.
				if(!this.IsActive() && Pony.IsActionActive(choice.Action)) {
					if(this.RandomChance(this.Ponyality.Laziness)) {
						undecided = lazy = true;
						continue;
					}
				}

				// pony do not like to flaunt their personality quirks, if we
				// selected one of the personality actions then there is a high
				// chance she should be undecided. this should make the quirky
				// actions more special when they do actually happen.
				switch(choice.Action) {
					case PonyAction.Action1:
						undecided = !this.RandomChance(this.Ponyality.Quirkiness);
						continue;
					case PonyAction.Action2:
						undecided = !this.RandomChance(this.Ponyality.Quirkiness);
						continue;
					case PonyAction.Passive1:
						undecided = !this.RandomChance(this.Ponyality.Quirkiness);
						continue;
				}
				
				// pony that can teleport generally do not teleport that often. i assume
				// the action takes mana or something, lol.			
				if(choice.Action == PonyAction.Teleport) {
					if(this.RandomChance(30)) {
						undecided = true;
						continue;
					}
				}

				// pony may tire easy. if she is doing something active and has chosen to
				// continue to be active, there is a chance she is too tired and will be
				// lazy instead.
				if(this.IsActive() && Pony.IsActionActive(choice.Action)) {
					if(!this.RandomChance(this.Ponyality.Stamina)) {
						undecided = lazy = true;
						continue;
					}
				}

			} while(undecided);

			return choice;
		}

		///////////////////////////////////////////////////////////////////////
		// action management methods //////////////////////////////////////////

		/// <summary>
		/// start a new action. this will stop any current actions, load the
		/// new image for the current action, and then execute the specific
		/// actions that she was to do.
		/// </summary>
		public void StartAction() {

			// stop any current action.
			this.StopAction();

			// load the new image.
			this.LoadImage();

			// most animations should repeat forever. ones that want to loop
			// once and then callback will set that themselves after.
			this.Window.AnimateForever();

			// place the window just above the task bar so it looks like they
			// be walkin on it. this might be broken on multi-head or non
			// bottom taskbars.
			this.Window.Top = SystemParameters.MaximizedPrimaryScreenHeight - this.Window.Height - this.YOffset;

			switch(Action) {
				case PonyAction.Trot:
					this.Trot();
					break;
				case PonyAction.Teleport:
					this.Teleport();
					break;
			}
		}

		/// <summary>
		/// stop any timers that may be involved with powering any of the
		/// actions.
		/// </summary>
		public void StopAction() {
			if(this.WindowTimer != null) {
				this.WindowTimer.Stop();
				this.WindowTimer = null;
			}
		}

		/// <summary>
		/// temporarily stop the pony from making her own decisions whenever
		/// she feels like it.
		/// </summary>
		public void PauseChoiceEngine() {
			if(this.ChoiceTimer != null && this.ChoiceTimer.IsEnabled) {
				Trace.WriteLine(String.Format("<< {0} holds off on making any more choices for now",this.Name));
				this.ChoiceTimer.Stop();
			}
		}

		/// <summary>
		/// allow the pony to resume making her own decisions whenever she
		/// feels like it.
		/// </summary>
		public void ResumeChoiceEngine() {

			// if the pony is doing something active while told to be still
			// she might still be performing an action to prepare. we do not
			// want to distract her. (stopping things like mousein mouseout
			// from restarting the choice engine while on a mission)
			if(this.Mode == PonyMode.Still && this.IsActive())
				return;

			// if she is being clingy then she is too busy to get distracted
			// and wander around.
			if(this.Mode == PonyMode.Clingy)
				return;

			// else it is ok to restart the choice timer.
			if(!this.ChoiceTimer.IsEnabled) {
				Trace.WriteLine(String.Format("<< {0} left to her own devices",this.Name));
				this.ChoiceTimer.Start();
			}
		}

		/// <summary>
		/// reset her energy reserve.
		/// </summary>
		public void EnergyReset() {
			this.Energy = 400;
		}

		/// <summary>
		/// mark energy as spent.
		/// </summary>
		public void EnergySpend() {
			if(this.IsActive()) this.Energy -= 1;
			else this.Energy -= 0.5;
		}

		///////////////////////////////////////////////////////////////////////
		// PonyMode.Clingy ////////////////////////////////////////////////////

		/// <summary>
		/// toggle clingy mode on or off.
		/// </summary>
		public void ClingToggle() {
			if(this.Mode == PonyMode.Clingy) {
				this.ClingStop();
			} else {
				this.ClingStart();
			}
		}

		/// <summary>
		/// put the pony in start clingy mode. when she is in this mood then
		/// she will continously follow the mouse cursor wherever it goes.
		/// </summary>
		private void ClingStart() {
			Trace.WriteLine(String.Format("// pony {0} get an obsessive look in her eye...", this.Name));

			this.StopAction();
			this.PauseChoiceEngine();

			this.Mode = PonyMode.Clingy;
			this.ClingTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, this.Window.Dispatcher);
			this.ClingTimer.Interval = TimeSpan.FromMilliseconds(250);
			this.ClingTimer.Tick += new EventHandler(this.ClingTick);
			this.ClingTimer.Start();
		}

		/// <summary>
		/// stop clingy mode.
		/// </summary>
		private void ClingStop() {
			Trace.WriteLine(String.Format("// pony {0} decides she doesn't need you.",this.Name));

			this.ClingTimer.Stop();
			this.ClingTimer = null;

			// if we were in clingy mode reset to free range. this is so we
			// can have the timer catch itself when a menu item is clicked
			// or something.
			if(this.Mode == PonyMode.Clingy) {
				this.Mode = PonyMode.Free;
				this.ResumeChoiceEngine();
			}
		}

		/// <summary>
		/// this does the work for determining which way she should trot off
		/// to try and catch the cusor, if she should at all. this is based
		/// on distance to the cursor.
		/// 
		/// TODO: find a way that does not require Forms. the WPF version of
		/// this returns bunk values if the cursor is not currently over the
		/// window. :(
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void ClingTick(Object sender, EventArgs e) {

			if(this.Mode != PonyMode.Clingy)
				this.ClingStop();

			double dist = ((this.Window.Left + (this.Window.Width / 2)) - System.Windows.Forms.Control.MousePosition.X);

			if(dist < 0 && Math.Abs(dist) >= this.Window.Width) {
				this.TellWhatDo(PonyAction.Trot, PonyDirection.Right);
			} else if(dist < 0) {
				this.TellWhatDo(PonyAction.Stand, PonyDirection.Right);
			}

			if(dist > 0 && Math.Abs(dist) >= this.Window.Width) {
				this.TellWhatDo(PonyAction.Trot, PonyDirection.Left);
			} else if(dist > 0) {
				this.TellWhatDo(PonyAction.Stand, PonyDirection.Left);
			}

		}

		///////////////////////////////////////////////////////////////////////
		// PonyAction.Trot ////////////////////////////////////////////////////

		/// <summary>
		/// put the pony in trotting mode. she will traverse the screen in
		/// whichever direction she is currently facing.
		/// </summary>
		private void Trot() {
			// inialize the timer which will run the trot animation of the window movement.
			this.WindowTimer = new DispatcherTimer(DispatcherPriority.Loaded, this.Window.Dispatcher);
			this.WindowTimer.Interval = TimeSpan.FromMilliseconds(25);
			this.WindowTimer.Tick += new EventHandler(this.TrotTick);
			this.WindowTimer.Start();
		}

		/// <summary>
		/// this does the work of making the pony traverse across the screen.
		/// it also detects if she has bumped into a wall (edge of the screen)
		/// and should change directions.
		/// 
		/// if the pony has been put into Still mode then she will switch to
		/// the standing action when hitting the wall.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void TrotTick(Object sender, EventArgs e) {
			PonyDirection direction = Direction;

			// figure out if she has wallfaced and needs to change directions.
			if(this.Direction == PonyDirection.Right) {
				if(((this.Window.Left + this.Window.Width) + 4) >= SystemParameters.PrimaryScreenWidth) {
					direction = PonyDirection.Left;
				}
			} else if(this.Direction == PonyDirection.Left) {
				if((this.Window.Left - 4) <= 0) {
					direction = PonyDirection.Right;
				}
			}

			// update the window position.
			this.Window.Left += (4 * (int)direction);

			if(direction != this.Direction) {
				Trace.WriteLine(String.Format(
					">> {0} has hit the {1} wall",
					this.Name,
					this.Direction.ToString()
				));

				// if she was trotting even though she had been told to stand
				// still, now that she has hit the wall she will turn around,
				// stand there, and look pretty.
				if(this.Mode == PonyMode.Still) {
					this.TellWhatDo(PonyAction.Stand, direction, false);
				}

				// else about face and keep going like a boss.
				else {
					this.TellWhatDo(PonyAction.Trot, direction);
				}
			}

		}

		/////////////////////////////////////////////////////////////////////////////
		// PonyAction.Teleport //////////////////////////////////////////////////////

		/// <summary>
		/// prepare the pony for transport.
		/// </summary>
		public void Teleport() {
			// do not make any more choices until the teleport sequence is over.
			this.Window.AnimateOnce();
			this.PauseChoiceEngine();
		}

		/// <summary>
		/// this is called when the teleport (out) animation ends. it handles
		/// moving the pony across the screen and staging teleporting back in.
		/// </summary>
		public void TeleportStage() {
			int oldpos = (int)this.Window.Left;
			PonyDirection dir = PonyDirection.None;

			// yoink.
			this.Window.Hide();

			// make sure she at least went some distance.
			do this.Window.PlaceRandomlyX();
			while(Math.Abs(oldpos - this.Window.Left) < this.Window.Width);

			// if she teleported to the right, face right. left, left.
			// not technically correct as twilight has demonstrated the ability
			// to rapid teleport side to side facing inwards each time, but on
			// our 2d plane here this just looks nicer.
			if(oldpos - this.Window.Left <= 0) dir = PonyDirection.Right;
			else dir = PonyDirection.Left;

			// start the second half of the teleport sequence.
			this.TellWhatDo(PonyAction.Teleport2,dir);
			this.Window.AnimateOnce();

			// boink.
			this.Window.Show();
		}

		/// <summary>
		/// this is called with the teleporting (in) animation ends. return
		/// the pony to normal behaviour.
		/// </summary>
		public void TeleportFinish() {
			this.ResumeChoiceEngine();
			this.TellWhatDo(PonyAction.Stand,this.Direction);
		}

		///////////////////////////////////////////////////////////////////////
		// image management methods ///////////////////////////////////////////

		/// <summary>
		/// bring all the gifs for this pony into ram so the disk doesn't
		/// get warm like it did last night when i left the mane six runnning.
		/// </summary>
		private void LoadAllImages() {
			this.AvailableActions.ForEach(delegate(PonyAction action){
				this.Image.Add(new PonyImage(this.Name, action, PonyDirection.Left));
				this.Image.Add(new PonyImage(this.Name, action, PonyDirection.Right));

				if(action == PonyAction.Teleport) {
					this.Image.Add(new PonyImage(this.Name, PonyAction.Teleport2, PonyDirection.Left));
					this.Image.Add(new PonyImage(this.Name, PonyAction.Teleport2, PonyDirection.Right));
				}
			});
		}

		/// <summary>
		/// load the graphic for the action she is currently doing.
		/// </summary>
		private void LoadImage() {
			this.LoadImage(this.Action, this.Direction);
		}

		/// <summary>
		/// find and apply a specified image from the pony's cache of them.
		/// </summary>
		/// <param name="action"></param>
		/// <param name="direction"></param>
		private void LoadImage(PonyAction action, PonyDirection direction) {
			PonyImage image;

			// find the requested image from the cache.
			image = this.Image.Find(delegate(PonyImage img){
				if(img.Action == action && img.Direction == direction) return true;
				else return false;
			});

			if(image == null) {
				Trace.WriteLine(String.Format(
					"!! no image for {0} {1} {2}",
					this.Name,
					action.ToString(),
					direction.ToString()
				));

				return;
			};

			// and apply it to the pony window.
			image.ApplyToPonyWindow(this.Window);

		}

		/////////////////////////////////////////////////////////////////////////////
		// utility methods //////////////////////////////////////////////////////////

		/// <summary>
		/// is the action she is currently doing considered an active action?
		/// </summary>
		/// <returns></returns>
		public bool IsActive() {
			return Pony.IsActionActive(this.Action);
		}

		/// <summary>
		/// is the specified action considered an active action?
		/// </summary>
		/// <param name="action"></param>
		/// <returns></returns>
		public static bool IsActionActive(PonyAction action) {
			if(Enum.IsDefined(typeof(PonyActionActive), (int)action)) return true;
			else return false;
		}

		/// <summary>
		/// do a random chance rng pull given an integer percent value. meaning
		/// if there is a 42% chance to do something, give it 42.
		/// </summary>
		/// <param name="percent"></param>
		public bool RandomChance(int percent) {
			if(this.RNG.Next(1,101) <= percent) return true;
			else return false;
		}

	}

}
