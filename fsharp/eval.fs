module Eval

    open Node
    open Types

    type Env = Map<string, Node>

    let rec iterPairs f = function
        | Pair(first, second, t) ->
            f first second
            iterPairs f t
        | Empty -> ()
        | _ -> raise <| Error.errExpectedX "list or vector"

    let quasiquoteForm nodes =
        let transformNode f = function
            | Elements 1 [|a|] -> f a
            | _ -> raise <| Error.wrongArity ()
        let singleNode = transformNode (fun n -> n)
        let rec quasiquote node =
            match node with
            | Cons(Symbol("unquote"), rest) -> rest |> singleNode
            | Cons(Cons(Symbol("splice-unquote"), spliceRest), rest) ->
                List([Symbol("concat"); singleNode spliceRest; quasiquote rest])
            | Cons(h, t) -> List([Symbol("cons"); quasiquote h; quasiquote t])
            | n -> List([Symbol("quote"); n])
        List(nodes) |> transformNode quasiquote

    let quoteForm = function
        | [node] -> node
        | _ -> raise <| Error.wrongArity ()

    let rec eval_ast env = function
        | Symbol(sym) -> Env.get env sym
        | List(lst) -> lst |> List.map (eval env) |> List
        | Vector(seg) -> seg |> Seq.map (eval env) |> Array.ofSeq |> Node.ofArray
        | Map(map) -> map |> Map.map (fun k v -> eval env v) |> Map
        | node -> node

    and defBangForm env = function
        | [sym; form] ->
            match sym with
            | Symbol(sym) ->
                let node = eval env form
                Env.set env sym node
                node
            | _ -> raise <| Error.errExpectedX "symbol"
        | _ -> raise <| Error.wrongArity ()

    and setBinding env first second =
        let s = match first with 
                | Symbol(s) -> s 
                | _ -> raise <| Error.errExpectedX "symbol"
        let form = eval env second
        Env.set env s form

    and letStarForm outer = function
        | [bindings; form] ->
            let inner = Env.makeNew outer [] []
            let binder = setBinding inner
            match bindings with
            | List(_) | Vector(_) -> iterPairs binder bindings
            | _ -> raise <| Error.errExpectedX "list or vector"
            inner, form
        | _ -> raise <| Error.wrongArity ()

    and ifForm env = function
        | [condForm; trueForm; falseForm] -> ifForm3 env condForm trueForm falseForm
        | [condForm; trueForm] -> ifForm3 env condForm trueForm Nil
        | _ -> raise <| Error.wrongArity ()

    and ifForm3 env condForm trueForm falseForm =
        match eval env condForm with
        | Bool(false) | Nil -> falseForm
        | _ -> trueForm

    and doForm env = function
        | [a] -> a
        | a::rest ->
            eval env a |> ignore
            doForm env rest
        | _ -> raise <| Error.wrongArity ()

    and fnStarForm outer nodes =
        let makeFunc binds body =
            let f = fun nodes ->
                        let inner = Env.makeNew outer binds nodes
                        eval inner body
            Env.makeFunc f body binds outer

        match nodes with
        | [List(binds); body] -> makeFunc binds body
        | [Vector(seg); body] -> makeFunc (List.ofSeq seg) body
        | [_; _] -> raise <| Error.errExpectedX "bindings of list or vector"
        | _ -> raise <| Error.wrongArity ()

    and eval env = function
        | List(Symbol("def!")::rest) -> defBangForm env rest
        | List(Symbol("let*")::rest) -> 
            let inner, form = letStarForm env rest
            form |> eval inner
        | List(Symbol("if")::rest) -> ifForm env rest |> eval env
        | List(Symbol("do")::rest) -> doForm env rest |> eval env
        | List(Symbol("fn*")::rest) -> fnStarForm env rest
        | List(Symbol("quote")::rest) -> quoteForm rest
        | List(Symbol("quasiquote")::rest) -> quasiquoteForm rest |> eval env
        | List(_) as node ->
            let resolved = node |> eval_ast env
            match resolved with
            | List(Func(_, f, _, _, [])::rest) -> f rest
            | List(Func(_, _, body, binds, outer)::rest) ->
                let inner = Env.makeNew outer binds rest
                body |> eval inner
            | _ -> raise <| Error.errExpectedX "function"
        | node -> node |> eval_ast env
