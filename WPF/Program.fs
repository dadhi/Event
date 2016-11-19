﻿module Lloyd.WPF.Program

open System
open System.Windows
open Lloyd.Core
open Lloyd.WPF.NativeUI
open Lloyd.Domain
open Lloyd.Domain.Model
open Lloyd.Core.UI


[<EntryPoint;STAThread>]
let main _ =
    
    let oldUser = User.login "old user"

    let rand = Random()

    let kidStore =
        let store = Store.emptyMemoryStore()
        let randomAge() = rand.Next 17 |> byte |> Kid.Age
        let randomBehaviour() = Kid.Behaviour <| match rand.Next 3 with |0->Bad|1->Mixed|_->Good
        ["Porter Profit";"Simon Swatzell";"Harold Hamada";"Eldon Edman";"Silas Shotts";"Trent Torrez";"Kraig Knowlton";"Aaron Allender";"Evan Espino";"Heriberto Holliman";"Hugh Haro";"Newton Nagle";"Lowell Level";"Mohamed Mutter";"Douglas Delapena";"Russ River";"Boris Bertin";"Rod Ruyle";"Anthony Aguiar";"Louis Lavelle";"Francisca Fung";"Irena Ines";"Geralyn Groseclose";"Sadye Selby";"Kati Kingsley";"Nelia Nimmons";"Annita Ashbrook";"Vilma Villalobos";"Stephania Symons";"Shirely Sweitzer";"Delphia Devilbiss";"Dodie Danko";"Arvilla Alcazar";"Sherlyn Shawgo";"Terresa Tygart";"Ines Izzo";"Mirian Markert";"Sheena Slover";"Ethelene Ebinger";"Cammie Croslin"]
        |> List.iter (fun n -> Store.create oldUser (List1.init (Kid.Name n) [randomAge();randomBehaviour()]) store |> ignore)
        store

    let toyStore =
        let store = Store.emptyMemoryStore()
        let randomAgeRange() = Toy.AgeRange <| match rand.Next 5 with |0->0uy,4uy|1->3uy,7uy|2->6uy,10uy|3->9uy,13uy|_->12uy,16uy
        let randomWorkRequired() = rand.Next(50,150) |> uint16 |> Toy.WorkRequired
        ["Playmobil";"Smurfs";"Toy Soldier";"Transformers";"My Little Pony";"Corgi Car";"Lego";"Meccano";"Stickle Bricks";"Play-Doh";"Rainbow Loom";"Spirograph";"Lego Mindstorms";"Speak & Spell";"Ant Farm";"Dominoes";"Risk";"Mouse Trap";"Xbox";"Trivial Pursuit";"Scrabble";"Monopoly";"Mr Potato Head";"Rubik's Cube";"Jigsaw Puzzle";"Chemistry Set";"Kaleidoscope";"Magna Doodle";"Etch A Sketch";"Toy Piano";"Gyroscope";"Hula Hoop";"Yo-Yo";"Frisbee";"Whistle";"Water Gun";"Slinky";"Roller Skates";"Marbles";"Tea Set"]
        |> List.iter (fun n -> Store.create oldUser (List1.init (Toy.Name n) [randomAgeRange();randomWorkRequired()]) store |> ignore)
        store

    let elfStore =
        let store = Store.emptyMemoryStore()
        let randomWorkRate() = rand.Next(5,15) |> uint16 |> Elf.WorkRate
        ["Brandybutter Cuddlebubbles";"Brandysnap Frostpie";"Tiramisu Stripycane";"Sugarmouse Brandypears";"Pompom Glittertrifle";"Eggnog Ivysocks";"Sherry Twinkletrifle";"Sugarplum Gingerberry";"Clementine Starstockings";"Cinnamon Sugartree";"Bluebell Fruitsnaps";"Clove Starfig";"Figgy Icicleleaves";"Florentine Snoozybaubles";"Garland Mullingsleigh";"Merry Goldenspice";"Hazelnut Sparklefir";"Nutmeg Jinglecrackers";"Noel Chocolatetoes";"Robin Glittercrystals"]
        |> List.iter (fun n -> Store.create oldUser (List1.init (Elf.Name n) [randomWorkRate()]) store |> ignore)
        store

    let kidObservable = Store.observable kidStore
    let toyObservable = Store.observable toyStore
    let elfObservable = Store.observable elfStore

    let toyProgressObservable =
        Query.toyProgress kidObservable toyObservable elfObservable
        |> Observable.cacheLast

    let mainWindow = Window()

    let you = User.login "you"

    let openApp app =
        mainWindow.Dispatcher.Invoke (fun () ->
            let window = Window()
            WPF.CreateNaiveUI window |> UI.run app
            window.Show()
        )

    let saveHandler store createMsg updateMsg ((oid,events),lastEvent) =
        match oid with
        | None -> Store.create you events store |> createMsg |> Some
        | Some aid -> Store.update you aid events lastEvent store |> updateMsg |> Some

    let commandHandler cmd =
        match cmd with
        | Apps.Cmd.OpenKidEdit kid -> Apps.KidEdit.app kid kidObservable toyObservable (saveHandler kidStore Apps.KidEdit.CreateResult Apps.KidEdit.UpdateResult) |> openApp
        | Apps.Cmd.OpenToyEdit toy -> Apps.ToyEdit.app toy toyObservable (saveHandler toyStore Apps.ToyEdit.CreateResult Apps.ToyEdit.UpdateResult) |> openApp
        | Apps.Cmd.OpenElfEdit elf -> Apps.ElfEdit.app elf elfObservable toyObservable (saveHandler elfStore Apps.ElfEdit.CreateResult Apps.ElfEdit.UpdateResult) |> openApp
        None

    let app = Apps.Main.app kidObservable toyObservable elfObservable toyProgressObservable commandHandler

    use kids = Procs.kidsRun kidStore toyStore
    
    use santa = Procs.santaRun toyStore elfStore toyProgressObservable

    WPF.CreateNaiveUI mainWindow |> UI.run app

    Application().Run(mainWindow) |> ignore
    
    0