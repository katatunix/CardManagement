﻿namespace CardManagement.Data
module CommandRepository =
    open CardManagement.Common.Errors
    open CardMongoConfiguration
    open CardDomainEntities
    open System
    open MongoDB.Driver
    open System.Threading.Tasks
    open FsToolkit.ErrorHandling
    open System.Linq.Expressions

    type CreateUserAsync = UserEntity -> IoResult<unit>
    type CreateCardAsync = CardEntity * CardAccountInfoEntity -> IoResult<unit>
    type ReplaceUserAsync = UserEntity -> IoResult<unit>
    type ReplaceCardAsync = CardEntity -> IoResult<unit>
    type ReplaceCardAccountInfoAsync = CardAccountInfoEntity -> IoResult<unit>
    type CreateBalanceOperationAsync = BalanceOperationEntity -> IoResult<unit>

    let updateOptions =
        let opt = ReplaceOptions()
        opt.IsUpsert <- false
        opt

    let private isDuplicateKeyException (ex: Exception) =
        ex :? MongoWriteException && (ex :?> MongoWriteException).WriteError.Category = ServerErrorCategory.DuplicateKey

    let rec private (|DuplicateKey|_|) (ex: Exception) =
        match ex with
        | :? MongoWriteException as ex when isDuplicateKeyException ex ->
            Some ex
        | :? MongoBulkWriteException as bex when bex.InnerException |> isDuplicateKeyException ->
            Some (bex.InnerException :?> MongoWriteException)
        | :? AggregateException as aex when aex.InnerException |> isDuplicateKeyException ->
            Some (aex.InnerException :?> MongoWriteException)
        | _ -> None

    let inline private executeInsertAsync (func: 'a -> Async<unit>) arg =
        async {
            try
                do! func(arg)
                return Ok ()
            with
            | DuplicateKey _ex ->
                    return EntityAlreadyExists (arg.GetType().Name, (entityId arg)) |> Error
        }

    let inline private executeReplaceAsync (update: _ -> Task<ReplaceOneResult>) arg =
        async {
            let! updateResult =
                update(idComparer arg, arg, updateOptions) |> Async.AwaitTask
            if not updateResult.IsAcknowledged then
                return sprintf "Update was not acknowledged for %A" arg |> failwith
            elif updateResult.MatchedCount = 0L then
                return EntityNotFound (arg.GetType().Name, entityId arg) |> Error
            else return Ok()
        }

    let createUserAsync (mongoDb : MongoDb) : CreateUserAsync =
        fun userEntity ->
        let insertUser = mongoDb.GetCollection(userCollection).InsertOneAsync >> Async.AwaitTask
        userEntity |> executeInsertAsync insertUser

    let createCardAsync (mongoDb: MongoDb) : CreateCardAsync =
        fun (card, accountInfo) ->
        let insertCardCommand = mongoDb.GetCollection(cardCollection).InsertOneAsync >> Async.AwaitTask
        let insertAccInfoCommand =
            mongoDb.GetCollection(cardAccountInfoCollection).InsertOneAsync >> Async.AwaitTask
        asyncResult {
            do! card |> executeInsertAsync insertCardCommand
            do! accountInfo |> executeInsertAsync insertAccInfoCommand
        }

    let replaceUserAsync (mongoDb: MongoDb) : ReplaceUserAsync =
        fun user ->
            let replaceCommand (selector: Expression<_>, user, options: ReplaceOptions) =
                mongoDb.GetCollection(userCollection).ReplaceOneAsync(selector, user, options)
            user |> executeReplaceAsync replaceCommand

    let replaceCardAsync (mongoDb: MongoDb) : ReplaceCardAsync =
        fun card ->
            let replaceCommand (selector: Expression<_>, card, options: ReplaceOptions) =
                mongoDb.GetCollection(cardCollection).ReplaceOneAsync(selector, card, options)
            card |> executeReplaceAsync replaceCommand

    let replaceCardAccountInfoAsync (mongoDb: MongoDb) : ReplaceCardAccountInfoAsync =
        fun accInfo ->
            let replaceCommand (selector: Expression<_>, accInfo, options: ReplaceOptions) =
                mongoDb.GetCollection(cardAccountInfoCollection).ReplaceOneAsync(selector, accInfo, options)
            accInfo |> executeReplaceAsync replaceCommand

    let createBalanceOperationAsync (mongoDb: MongoDb) : CreateBalanceOperationAsync =
        fun balanceOperation ->
            let insert = mongoDb.GetCollection(balanceOperationCollection).InsertOneAsync >> Async.AwaitTask
            balanceOperation |> executeInsertAsync insert
