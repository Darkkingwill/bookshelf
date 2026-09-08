import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';

// Lets the user override a book's series title/position, force which linked
// series is treated as primary, manually link this book into a series the
// metadata provider missed, or unlink one it got wrong - for when the
// metadata provider gets it wrong (junk boxset titles, ASINs baked into
// series names, mismatched series links, missing/incorrect series
// membership, etc). Overrides and manual links are saved directly via the
// API and are not part of the standard book edit form/save flow.
class SeriesLinkEditor extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isFetching: false,
      isPopulated: false,
      error: null,
      isSaving: false,
      saveError: null,
      saveSuccess: false,
      links: [],
      availableSeries: [],
      newSeriesId: '',
      newPosition: '',
      isAdding: false,
      addError: null,
      removingId: null,
      removeError: null
    };
  }

  componentDidMount() {
    this.fetchLinks();
    this.fetchAvailableSeries();
  }

  //
  // Control

  fetchLinks() {
    const {
      bookId
    } = this.props;

    this.setState({ isFetching: true, error: null });

    const { request } = createAjaxRequest({
      url: `/series/link?bookIds=${bookId}`,
      method: 'GET',
      dataType: 'json'
    });

    request.done((data) => {
      this.setState({
        isFetching: false,
        isPopulated: true,
        links: data
      });
    });

    request.fail((xhr) => {
      this.setState({
        isFetching: false,
        isPopulated: false,
        error: xhr
      });
    });
  }

  fetchAvailableSeries() {
    const {
      bookId
    } = this.props;

    const { request: bookRequest } = createAjaxRequest({
      url: `/book/${bookId}`,
      method: 'GET',
      dataType: 'json'
    });

    bookRequest.done((book) => {
      const { request: seriesRequest } = createAjaxRequest({
        url: `/series?authorId=${book.authorId}`,
        method: 'GET',
        dataType: 'json'
      });

      seriesRequest.done((data) => {
        this.setState({ availableSeries: data });
      });
    });
  }

  updateLink(index, changes) {
    this.setState((prevState) => {
      const links = [...prevState.links];
      links[index] = { ...links[index], ...changes };

      return { links, saveSuccess: false };
    });
  }

  //
  // Listeners

  onTitleOverrideChange = ({ value }, index) => {
    this.updateLink(index, { titleOverride: value === '' ? null : value });
  };

  onPositionOverrideChange = ({ value }, index) => {
    this.updateLink(index, { positionOverride: value === '' ? null : value });
  };

  onPrimaryOverrideChange = (event, index) => {
    const raw = event.target.value;
    const isPrimaryOverride = raw === '' ? null : raw === 'true';

    this.updateLink(index, { isPrimaryOverride });
  };

  onPinnedChange = (event, index) => {
    this.updateLink(index, { pinned: event.target.checked });
  };

  onSavePress = () => {
    const {
      links
    } = this.state;

    this.setState({ isSaving: true, saveError: null, saveSuccess: false });

    const { request } = createAjaxRequest({
      url: '/series/link',
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify(links)
    });

    request.done((data) => {
      this.setState({
        isSaving: false,
        saveSuccess: true,
        links: data
      });
    });

    request.fail((xhr) => {
      this.setState({
        isSaving: false,
        saveError: xhr
      });
    });
  };

  onNewSeriesChange = (event) => {
    this.setState({ newSeriesId: event.target.value });
  };

  onNewPositionChange = ({ value }) => {
    this.setState({ newPosition: value });
  };

  onAddPress = () => {
    const {
      bookId
    } = this.props;

    const {
      newSeriesId,
      newPosition
    } = this.state;

    if (!newSeriesId) {
      return;
    }

    this.setState({ isAdding: true, addError: null });

    const { request } = createAjaxRequest({
      url: '/series/link',
      method: 'POST',
      dataType: 'json',
      data: JSON.stringify({
        seriesId: parseInt(newSeriesId),
        bookId,
        position: newPosition || null,
        seriesPosition: 0,
        isPrimary: false
      })
    });

    request.done(() => {
      this.setState({
        isAdding: false,
        newSeriesId: '',
        newPosition: ''
      });
      this.fetchLinks();
    });

    request.fail((xhr) => {
      this.setState({
        isAdding: false,
        addError: xhr
      });
    });
  };

  onRemovePress = (linkId) => {
    this.setState({ removingId: linkId, removeError: null });

    const { request } = createAjaxRequest({
      url: `/series/link/${linkId}`,
      method: 'DELETE'
    });

    request.done(() => {
      this.setState((prevState) => ({
        removingId: null,
        links: prevState.links.filter((link) => link.id !== linkId)
      }));
    });

    request.fail((xhr) => {
      this.setState({
        removingId: null,
        removeError: xhr
      });
    });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      isPopulated,
      error,
      links,
      isSaving,
      saveError,
      saveSuccess,
      availableSeries,
      newSeriesId,
      newPosition,
      isAdding,
      addError,
      removingId,
      removeError
    } = this.state;

    if (isFetching) {
      return <LoadingIndicator />;
    }

    if (error) {
      return (
        <Alert kind={kinds.DANGER}>
          Unable to load series links for this book
        </Alert>
      );
    }

    const linkedSeriesIds = links.map((link) => link.seriesId);
    const seriesOptions = availableSeries.filter((series) => !linkedSeriesIds.includes(series.id));

    return (
      <div>
        {
          isPopulated && !!links.length &&
            <p style={{ opacity: 0.8, marginBottom: 10 }}>
              Override the series title, position, or which linked series is used for renaming, when the metadata provider gets it wrong. Leave a field blank to fall back to the provider value. Pin a link to keep it even if the metadata provider stops reporting it. Overrides and pins survive future metadata refreshes.
            </p>
        }

        {
          isPopulated && !links.length &&
            <Alert kind={kinds.INFO}>
              This book is not linked to any series
            </Alert>
        }

        {
          links.map((link, index) => {
            const effectivePosition = link.effectivePosition;
            const effective = effectivePosition ? `${link.effectiveTitle} #${effectivePosition}` : link.effectiveTitle;

            return (
              <div
                key={link.id}
                style={{
                  border: '1px solid rgba(128, 128, 128, 0.4)',
                  borderRadius: 4,
                  padding: 10,
                  marginBottom: 10
                }}
              >
                <div style={{ fontWeight: 'bold', marginBottom: 8, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <span>
                    {link.seriesTitle}
                    {
                      link.isPrimary &&
                        <span style={{ fontWeight: 'normal', opacity: 0.7 }}> (provider marks this primary)</span>
                    }
                  </span>

                  <Button
                    kind={kinds.DANGER}
                    isDisabled={removingId === link.id}
                    onPress={() => this.onRemovePress(link.id)}
                  >
                    {removingId === link.id ? 'Removing...' : 'Remove from series'}
                  </Button>
                </div>

                <div style={{ display: 'flex', gap: 10, marginBottom: 8, flexWrap: 'wrap' }}>
                  <label style={{ flex: '2 1 200px' }}>
                    Title override
                    <TextInput
                      name={`titleOverride-${index}`}
                      value={link.titleOverride || ''}
                      placeholder={link.seriesTitle || ''}
                      onChange={(payload) => this.onTitleOverrideChange(payload, index)}
                    />
                  </label>

                  <label style={{ flex: '1 1 100px' }}>
                    Position override
                    <TextInput
                      name={`positionOverride-${index}`}
                      value={link.positionOverride || ''}
                      placeholder={link.position || ''}
                      onChange={(payload) => this.onPositionOverrideChange(payload, index)}
                    />
                  </label>

                  <label style={{ flex: '1 1 150px' }}>
                    Use as primary series
                    <select
                      style={{ width: '100%', height: 30 }}
                      value={link.isPrimaryOverride === null || link.isPrimaryOverride === undefined ? '' : String(link.isPrimaryOverride)}
                      onChange={(event) => this.onPrimaryOverrideChange(event, index)}
                    >
                      <option value="">Automatic</option>
                      <option value="true">Force primary</option>
                      <option value="false">Force not primary</option>
                    </select>
                  </label>

                  <label style={{ flex: '1 1 100px', display: 'flex', alignItems: 'flex-end', gap: 4 }}>
                    <input
                      type="checkbox"
                      checked={!!link.pinned}
                      onChange={(event) => this.onPinnedChange(event, index)}
                    />
                    Pinned
                  </label>
                </div>

                <div style={{ fontSize: 12, opacity: 0.7 }}>
                  Will rename as: {effective}
                </div>
              </div>
            );
          })
        }

        {
          removeError &&
            <Alert kind={kinds.DANGER}>
              Unable to remove this book from the series
            </Alert>
        }

        {
          saveError &&
            <Alert kind={kinds.DANGER}>
              Unable to save series overrides
            </Alert>
        }

        {
          saveSuccess &&
            <Alert kind={kinds.SUCCESS}>
              Series overrides saved. Use Rename Files / Rename Author to apply them on disk.
            </Alert>
        }

        {
          !!links.length &&
            <SpinnerButton
              isSpinning={isSaving}
              onPress={this.onSavePress}
            >
              Save Series Overrides
            </SpinnerButton>
        }

        <div
          style={{
            border: '1px dashed rgba(128, 128, 128, 0.4)',
            borderRadius: 4,
            padding: 10,
            marginTop: 10
          }}
        >
          <div style={{ fontWeight: 'bold', marginBottom: 8 }}>
            Add this book to another series
          </div>

          {
            !seriesOptions.length &&
              <div style={{ opacity: 0.7, fontSize: 12 }}>
                No other series found for this author.
              </div>
          }

          {
            !!seriesOptions.length &&
              <div style={{ display: 'flex', gap: 10, alignItems: 'flex-end', flexWrap: 'wrap' }}>
                <label style={{ flex: '2 1 200px' }}>
                  Series
                  <select
                    style={{ width: '100%', height: 30 }}
                    value={newSeriesId}
                    onChange={this.onNewSeriesChange}
                  >
                    <option value="">Select a series...</option>
                    {
                      seriesOptions.map((series) => {
                        return (
                          <option key={series.id} value={series.id}>
                            {series.title}
                          </option>
                        );
                      })
                    }
                  </select>
                </label>

                <label style={{ flex: '1 1 100px' }}>
                  Position
                  <TextInput
                    name="newPosition"
                    value={newPosition}
                    placeholder="e.g. 3"
                    onChange={this.onNewPositionChange}
                  />
                </label>

                <SpinnerButton
                  isSpinning={isAdding}
                  isDisabled={!newSeriesId}
                  onPress={this.onAddPress}
                >
                  Add
                </SpinnerButton>
              </div>
          }

          {
            addError &&
              <Alert kind={kinds.DANGER}>
                Unable to add this book to the series
              </Alert>
          }
        </div>
      </div>
    );
  }
}

SeriesLinkEditor.propTypes = {
  bookId: PropTypes.number.isRequired
};

export default SeriesLinkEditor;
