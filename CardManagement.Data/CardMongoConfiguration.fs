﻿namespace CardManagement.Data

module CardMongoConfiguration =

    open CardManagement.Common
    open MongoDB.Driver

    type MongoSettings =
        { Database: string
          Host: string
          Port: int
          User: string
          Password: string }
        with
        member this.ConnectionString =
            if this.User |> isNullOrEmpty then sprintf "mongodb://%s:%i" this.Host this.Port
            else sprintf "mongodb://%s:%s@%s:%i" this.User this.Password this.Host this.Port

    let private createClient (connectionString:string) = new MongoClient(connectionString)

    let getDatabase (config: MongoSettings) =
        let client = createClient config.ConnectionString
        client.GetDatabase(config.Database)

    let getSession (config: MongoSettings) =
        let client = createClient config.ConnectionString
        client.StartSession()

    type MongoDb = IMongoDatabase

    let [<Literal>] internal cardCollection = "Card"
    let [<Literal>] internal userCollection = "User"
    let [<Literal>] internal cardAccountInfoCollection = "cardAccountInfo"
    let [<Literal>] internal balanceOperationCollection = "BalanceOperation"

    type CardNumberString = string
