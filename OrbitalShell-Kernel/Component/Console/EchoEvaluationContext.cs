﻿using OrbitalShell.Component.CommandLine.Processor;

namespace OrbitalShell.Component.Console
{
    /// <summary>
    /// contextual data of an echo operation context
    /// </summary>
    public class EchoEvaluationContext
    {
        /// <summary>
        /// target stream
        /// </summary>
        public ConsoleTextWriterWrapper Out;

        /// <summary>
        /// command evaluation context
        /// </summary>
        public CommandEvaluationContext CommandEvaluationContext;

        /// <summary>
        /// formatting options
        /// </summary>
        public FormatingOptions Options;

        public EchoEvaluationContext(
            ConsoleTextWriterWrapper @out,
            CommandEvaluationContext cmdContext,
            FormatingOptions options = null)
        {
            Out = @out;
            CommandEvaluationContext = cmdContext;
            Options = options;
        }

        public EchoEvaluationContext(CommandEvaluationContext context)
        {
            Out = context.Out;
            CommandEvaluationContext = context;
            Options = new FormatingOptions();
        }

        public EchoEvaluationContext(
            EchoEvaluationContext ctx
            )
        {
            Out = ctx.Out;
            CommandEvaluationContext = ctx.CommandEvaluationContext;
            Options = ctx.Options;
        }

        public EchoEvaluationContext(
            EchoEvaluationContext ctx,
            FormatingOptions options
            )
        {
            Out = ctx.Out;
            CommandEvaluationContext = ctx.CommandEvaluationContext;
            Options = options;
        }

        /// <summary>
        ///  for Lib.TypeExt.Clone() method purpose
        /// </summary>
        public EchoEvaluationContext() { }

        public void Deconstruct(
            out ConsoleTextWriterWrapper @out,
            out CommandEvaluationContext context,
            out FormatingOptions options)
        {
            @out = Out;
            context = CommandEvaluationContext;
            options = Options;
        }
    }
}
