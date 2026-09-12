// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Windows.Input;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class QuickTourViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IThemeService _themeService;

        private int _currentStep = 1;
        private const int TotalSteps = 4;

        public event Action? RequestClose;

        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (SetProperty(ref _currentStep, value))
                {
                    OnPropertyChanged(nameof(IsFirstStep));
                    OnPropertyChanged(nameof(IsLastStep));
                    OnPropertyChanged(nameof(StepIndicatorText));
                    OnPropertyChanged(nameof(IsStep1));
                    OnPropertyChanged(nameof(IsStep2));
                    OnPropertyChanged(nameof(IsStep3));
                    OnPropertyChanged(nameof(IsStep4));
                }
            }
        }

        public bool IsFirstStep => CurrentStep == 1;
        public bool IsLastStep => CurrentStep == TotalSteps;
        public string StepIndicatorText => $"Step {CurrentStep} of {TotalSteps}";

        public bool IsStep1 => CurrentStep == 1;
        public bool IsStep2 => CurrentStep == 2;
        public bool IsStep3 => CurrentStep == 3;
        public bool IsStep4 => CurrentStep == 4;

        public ICommand NextStepCommand { get; }
        public ICommand PrevStepCommand { get; }
        public ICommand SkipTourCommand { get; }
        public ICommand FinishTourCommand { get; }

        public QuickTourViewModel(IConfigurationService configService, IThemeService themeService)
        {
            _configService = configService;
            _themeService = themeService;

            NextStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep < TotalSteps)
                {
                    CurrentStep++;
                }
                else
                {
                    CompleteTour();
                }
            });

            PrevStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep > 1)
                {
                    CurrentStep--;
                }
            });

            SkipTourCommand = new RelayCommand(CompleteTour);
            FinishTourCommand = new RelayCommand(CompleteTour);
        }

        private void CompleteTour()
        {
            _configService.CurrentConfig.HasCompletedQuickTour = true;
            _configService.SaveConfig();
            RequestClose?.Invoke();
        }
    }
}
