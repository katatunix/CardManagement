﻿namespace CardManagement

(*
This module contains business logic only. It doesn't know anything about data access layer,
it doesn't deal with composition, the only thing we have here is functions, which represent
business data transformations.
Note that all the functions here are pure and total: they don't deal with any kind of external state,
they have no side effects, and they can successfully process ANY kind of input - they don't throw exceptions.
Since they deal with domain types (which earlier we designed in a way that invalid data can't be represented with them),
we don't need to do input validation here. The only error we return in here is `OperationNotAllowedError`,
which means that user provided valid data but wants to do something that is not allowed, e.g. pay with expired card.
*)
module CardActions =
    open System
    open CardDomain
    open CardManagement.Common.Errors
    open CardManagement.Common

    let private isExpired (currentDate: DateTimeOffset) (month: Month, year: Year) =
        (int year.Value, month.ToNumber() |> int) < (currentDate.Year, currentDate.Month)

    let private setDailyLimitNotAllowed = operationNotAllowed "Set daily limit"

    let private processPaymentNotAllowed = operationNotAllowed "Process payment"

    let private cardExpiredMessage (cardNumber: CardNumber) =
        sprintf "Card %s is expired" cardNumber.Value

    let private cardDeactivatedMessage (cardNumber: CardNumber) =
        sprintf "Card %s is deactivated" cardNumber.Value

    let isCardExpired (currentDate: DateTimeOffset) card =
        isExpired currentDate card.Expiration

    let deactivate card =
        match card.AccountDetails with
        | Deactivated -> card
        | Active _ -> { card with AccountDetails = Deactivated }

    let activate (cardAccountInfo: AccountInfo) card =
        match card.AccountDetails with
        | Active _ -> card
        | Deactivated -> { card with AccountDetails = Active cardAccountInfo }

    let setDailyLimit (currentDate: DateTimeOffset) limit card =
        if isCardExpired currentDate card then
            cardExpiredMessage card.CardNumber |> setDailyLimitNotAllowed
        else
        match card.AccountDetails with
        | Deactivated -> cardDeactivatedMessage card.CardNumber |> setDailyLimitNotAllowed
        | Active accInfo -> { card with AccountDetails = Active { accInfo with DailyLimit = limit } } |> Ok

    let processPayment (currentDate: DateTimeOffset) (spentToday: Money) card (paymentAmount: MoneyTransaction) =
        if isCardExpired currentDate card then
            cardExpiredMessage card.CardNumber |> processPaymentNotAllowed
        else
        match card.AccountDetails with
        | Deactivated -> cardDeactivatedMessage card.CardNumber |> processPaymentNotAllowed
        | Active accInfo ->
            if paymentAmount.Value > accInfo.Balance.Value then
                sprintf "Insufficient funds on card %s" card.CardNumber.Value
                |> processPaymentNotAllowed
            else
            match accInfo.DailyLimit with
            | Limit limit when limit < spentToday + paymentAmount ->
                sprintf "Daily limit is exceeded for card %s with daily limit %M. Today was spent %M"
                    card.CardNumber.Value limit.Value spentToday.Value
                |> processPaymentNotAllowed
            (*
            We could use here the ultimate wild card case like this:
            | _ ->
            but it's dangerous because if a new case appears in `DailyLimit` type,
            we won't get a compile error here, which would remind us to process this
            new case in here. So this is a safe way to do the same thing.
            *)
            | Limit _ | Unlimited ->
                let newBalance = accInfo.Balance - paymentAmount
                let updatedCard = { card with AccountDetails = Active { accInfo with Balance = newBalance } }
                let balanceOperation =
                    { Timestamp = currentDate
                      CardNumber = card.CardNumber
                      NewBalance = newBalance
                      BalanceChange = Decrease paymentAmount }
                Ok (updatedCard, balanceOperation)

    let topUp (currentDate: DateTimeOffset) card (topUp : MoneyTransaction) =
        let topUpNotAllowed = operationNotAllowed "Top up"
        if isCardExpired currentDate card then
            cardExpiredMessage card.CardNumber |> topUpNotAllowed
        else
        match card.AccountDetails with
        | Deactivated -> cardDeactivatedMessage card.CardNumber |> topUpNotAllowed
        | Active accInfo ->
            let newBalance = accInfo.Balance + topUp
            let updatedCard = { card with AccountDetails = Active { accInfo with Balance = newBalance } }
            let balanceOperation =
                { Timestamp = currentDate
                  NewBalance = newBalance
                  CardNumber = card.CardNumber
                  BalanceChange = Increase topUp }
            Ok (updatedCard, balanceOperation)
